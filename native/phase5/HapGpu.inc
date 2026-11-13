// SPDX-License-Identifier: MIT
// GPU BC texture sampling and Scaled YCoCg conversion (Hap specification).
static void PrepareHapPipeline(Output* o) {
    if(o->vertex)return;
    const char* shader=R"(
Texture2D image:register(t0); SamplerState imageSampler:register(s0);
struct V { float4 position:SV_POSITION; float2 uv:TEXCOORD0; };
V VS(uint id:SV_VertexID) { V v; v.uv=float2((id<<1)&2,id&2); v.position=float4(v.uv.x*2-1,1-v.uv.y*2,0,1); return v; }
float4 PSHap(V v):SV_TARGET {return float4(image.Sample(imageSampler,v.uv).rgb,1);}
float4 PSQ(V v):SV_TARGET {
 float4 t=image.Sample(imageSampler,v.uv);
 float scale=1+t.b*(255.0/8.0);
 float co=(t.r-128.0/255.0)/scale,cg=(t.g-128.0/255.0)/scale,y=t.a;
 return float4(y+co-cg,y+cg,y-co-cg,1);
}
)";
    ComPtr<ID3DBlob> vs,ps,pq,errors;
    Check(D3DCompile(shader,strlen(shader),nullptr,nullptr,nullptr,"VS","vs_4_0",0,0,&vs,&errors));
    Check(D3DCompile(shader,strlen(shader),nullptr,nullptr,nullptr,"PSHap","ps_4_0",0,0,&ps,&errors));
    Check(D3DCompile(shader,strlen(shader),nullptr,nullptr,nullptr,"PSQ","ps_4_0",0,0,&pq,&errors));
    Check(o->device->CreateVertexShader(vs->GetBufferPointer(),vs->GetBufferSize(),nullptr,&o->vertex));
    Check(o->device->CreatePixelShader(ps->GetBufferPointer(),ps->GetBufferSize(),nullptr,&o->hapShader));
    Check(o->device->CreatePixelShader(pq->GetBufferPointer(),pq->GetBufferSize(),nullptr,&o->qShader));
    D3D11_SAMPLER_DESC sd={};sd.Filter=D3D11_FILTER_MIN_MAG_MIP_LINEAR;
    sd.AddressU=sd.AddressV=sd.AddressW=D3D11_TEXTURE_ADDRESS_CLAMP;sd.MaxLOD=D3D11_FLOAT32_MAX;
    Check(o->device->CreateSamplerState(&sd,&o->sampler));
    D3D11_RASTERIZER_DESC rd={};rd.FillMode=D3D11_FILL_SOLID;rd.CullMode=D3D11_CULL_NONE;rd.DepthClipEnable=TRUE;
    Check(o->device->CreateRasterizerState(&rd,&o->raster));
    D3D11_TEXTURE2D_DESC td={};td.Width=o->width;td.Height=o->height;td.MipLevels=td.ArraySize=1;
    td.Format=DXGI_FORMAT_B8G8R8A8_UNORM;td.SampleDesc.Count=1;td.BindFlags=D3D11_BIND_RENDER_TARGET|D3D11_BIND_SHADER_RESOURCE;
    Check(o->device->CreateTexture2D(&td,nullptr,&o->rendered));
    Check(o->device->CreateRenderTargetView(o->rendered.Get(),nullptr,&o->renderedView));
}
extern "C" __declspec(dllexport) int __cdecl output_hap(void* handle,const unsigned char* blocks,int length,int width,int height,int kind) {
    auto o=(Output*)handle;
    if(!o || !blocks || width<=0 || height<=0 || width>8192 || height>8192 || width%4 || height%4 || (kind!=1 && kind!=2 && kind!=3) || length!=(kind==3?width*height*4:(width/4)*(height/4)*(kind==1?8:16)))return E_INVALIDARG;
    if(!o->offscreen && (!IsWindow(o->window)||(!o->embedded && !o->confirmClose && (GetAsyncKeyState(VK_ESCAPE)&0x8000))))return HRESULT_FROM_WIN32(ERROR_CANCELLED);
    try {
        PrepareHapPipeline(o);
        if(o->sourceWidth!=width || o->sourceHeight!=height || o->sourceKind!=kind) {
            o->compressedView.Reset();o->compressed.Reset();
            D3D11_TEXTURE2D_DESC td={};td.Width=width;td.Height=height;td.MipLevels=td.ArraySize=1;
            td.Format=kind==3?DXGI_FORMAT_B8G8R8A8_UNORM:kind==1?DXGI_FORMAT_BC1_UNORM:DXGI_FORMAT_BC3_UNORM;td.SampleDesc.Count=1;td.BindFlags=D3D11_BIND_SHADER_RESOURCE;
            Check(o->device->CreateTexture2D(&td,nullptr,&o->compressed));
            Check(o->device->CreateShaderResourceView(o->compressed.Get(),nullptr,&o->compressedView));
            o->sourceWidth=width;o->sourceHeight=height;o->sourceKind=kind;
        }
        o->context->UpdateSubresource(o->compressed.Get(),0,nullptr,blocks,kind==3?width*4:(width/4)*(kind==1?8:16),0);
        const float black[4]={0,0,0,1};o->context->ClearRenderTargetView(o->renderedView.Get(),black);
        float scale=std::min(o->width/(float)width,o->height/(float)height);
        D3D11_VIEWPORT vp={(o->width-width*scale)/2,(o->height-height*scale)/2,width*scale,height*scale,0,1};
        o->context->RSSetViewports(1,&vp);o->context->RSSetState(o->raster.Get());
        ID3D11RenderTargetView* target=o->renderedView.Get();o->context->OMSetRenderTargets(1,&target,nullptr);
        o->context->IASetInputLayout(nullptr);o->context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        o->context->VSSetShader(o->vertex.Get(),nullptr,0);o->context->PSSetShader(kind==2?o->qShader.Get():o->hapShader.Get(),nullptr,0);
        ID3D11ShaderResourceView* view=o->compressedView.Get();o->context->PSSetShaderResources(0,1,&view);
        ID3D11SamplerState* sampler=o->sampler.Get();o->context->PSSetSamplers(0,1,&sampler);o->context->Draw(3,0);
        view=nullptr;o->context->PSSetShaderResources(0,1,&view);o->context->OMSetRenderTargets(0,nullptr,nullptr);
        o->baseImage=o->rendered;return PresentComposite(o);
    } catch(HRESULT hr){return hr;} catch(...){return E_OUTOFMEMORY;}
}
extern "C" __declspec(dllexport) int __cdecl output_readback(void* handle,unsigned char* pixels,int length) {
    auto o=(Output*)handle;if(!o || !pixels || !o->last || length!=o->width*o->height*4)return E_INVALIDARG;
    try {
        D3D11_TEXTURE2D_DESC td;o->last->GetDesc(&td);td.Usage=D3D11_USAGE_STAGING;td.CPUAccessFlags=D3D11_CPU_ACCESS_READ;td.BindFlags=0;
        ComPtr<ID3D11Texture2D> staging;Check(o->device->CreateTexture2D(&td,nullptr,&staging));o->context->CopyResource(staging.Get(),o->last.Get());
        D3D11_MAPPED_SUBRESOURCE m;Check(o->context->Map(staging.Get(),0,D3D11_MAP_READ,0,&m));
        for(int y=0;y<o->height;++y)memcpy(pixels+y*o->width*4,(char*)m.pData+y*m.RowPitch,o->width*4);
        o->context->Unmap(staging.Get(),0);return S_OK;
    } catch(HRESULT hr){return hr;}
}


