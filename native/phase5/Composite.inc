// GPU-only cover composition. The decoder image remains separate from the composited result.
static void PrepareComposite(Output* o){
 if(!o->compositeVertex){
  const char* source=R"(
Texture2D video:register(t0);Texture2D cover:register(t1);SamplerState smp:register(s0);
cbuffer Params:register(b0){float mixAmount;float2 coverFactor;float unused;};
struct V{float4 p:SV_POSITION;float2 uv:TEXCOORD0;};
V VS(uint id:SV_VertexID){V v;v.uv=float2((id<<1)&2,id&2);v.p=float4(v.uv.x*2-1,1-v.uv.y*2,0,1);return v;}
float4 PS(V v):SV_TARGET{float2 uv=(v.uv-.5)*coverFactor+.5;float3 c=0;
 if(all(uv>=0)&&all(uv<=1))c=cover.Sample(smp,uv).rgb;
 return float4(lerp(video.Sample(smp,v.uv).rgb,c,mixAmount),1);})";
  ComPtr<ID3DBlob> vs,ps,error;Check(D3DCompile(source,strlen(source),nullptr,nullptr,nullptr,"VS","vs_4_0",0,0,&vs,&error));Check(D3DCompile(source,strlen(source),nullptr,nullptr,nullptr,"PS","ps_4_0",0,0,&ps,&error));
  Check(o->device->CreateVertexShader(vs->GetBufferPointer(),vs->GetBufferSize(),nullptr,&o->compositeVertex));Check(o->device->CreatePixelShader(ps->GetBufferPointer(),ps->GetBufferSize(),nullptr,&o->compositeShader));
  D3D11_SAMPLER_DESC sd={};sd.Filter=D3D11_FILTER_MIN_MAG_MIP_LINEAR;sd.AddressU=sd.AddressV=sd.AddressW=D3D11_TEXTURE_ADDRESS_CLAMP;sd.MaxLOD=D3D11_FLOAT32_MAX;Check(o->device->CreateSamplerState(&sd,&o->compositeSampler));
  D3D11_RASTERIZER_DESC rd={};rd.FillMode=D3D11_FILL_SOLID;rd.CullMode=D3D11_CULL_NONE;rd.DepthClipEnable=TRUE;Check(o->device->CreateRasterizerState(&rd,&o->compositeRaster));
  D3D11_BUFFER_DESC cb={};cb.ByteWidth=16;cb.Usage=D3D11_USAGE_DEFAULT;cb.BindFlags=D3D11_BIND_CONSTANT_BUFFER;Check(o->device->CreateBuffer(&cb,nullptr,&o->compositeConstants));
 }
 if(!o->compositeImage||o->compositeWidth!=o->width||o->compositeHeight!=o->height){
  o->compositeTarget.Reset();o->compositeImage.Reset();D3D11_TEXTURE2D_DESC td={};td.Width=o->width;td.Height=o->height;td.MipLevels=td.ArraySize=1;td.Format=DXGI_FORMAT_B8G8R8A8_UNORM;td.SampleDesc.Count=1;td.BindFlags=D3D11_BIND_RENDER_TARGET|D3D11_BIND_SHADER_RESOURCE;
  Check(o->device->CreateTexture2D(&td,nullptr,&o->compositeImage));Check(o->device->CreateRenderTargetView(o->compositeImage.Get(),nullptr,&o->compositeTarget));o->compositeWidth=o->width;o->compositeHeight=o->height;
 }
}
static int PresentComposite(Output* o){
 if(!o->baseImage)return S_FALSE;
 try{
  ComPtr<ID3D11Texture2D> result=o->baseImage;
  if(o->coverMix>0){
   PrepareComposite(o);ComPtr<ID3D11ShaderResourceView> video;Check(o->device->CreateShaderResourceView(o->baseImage.Get(),nullptr,&video));
   float scale=o->coverWidth>0?std::min(o->width/(float)o->coverWidth,o->height/(float)o->coverHeight):1;
   float params[4]={o->coverMix,o->coverWidth>0?o->width/(o->coverWidth*scale):1,o->coverHeight>0?o->height/(o->coverHeight*scale):1,0};o->context->UpdateSubresource(o->compositeConstants.Get(),0,nullptr,params,0,0);
   D3D11_VIEWPORT vp={0,0,(float)o->width,(float)o->height,0,1};o->context->RSSetViewports(1,&vp);o->context->RSSetState(o->compositeRaster.Get());
   auto rt=o->compositeTarget.Get();o->context->OMSetRenderTargets(1,&rt,nullptr);o->context->IASetInputLayout(nullptr);o->context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
   o->context->VSSetShader(o->compositeVertex.Get(),nullptr,0);o->context->PSSetShader(o->compositeShader.Get(),nullptr,0);ID3D11ShaderResourceView* views[]={video.Get(),o->coverView.Get()};o->context->PSSetShaderResources(0,2,views);
   auto sampler=o->compositeSampler.Get();o->context->PSSetSamplers(0,1,&sampler);auto cb=o->compositeConstants.Get();o->context->PSSetConstantBuffers(0,1,&cb);o->context->Draw(3,0);
   ID3D11ShaderResourceView* clear[2]={};o->context->PSSetShaderResources(0,2,clear);o->context->OMSetRenderTargets(0,nullptr,nullptr);result=o->compositeImage;
  }
  o->last=result;
  if(!o->swap||(o->offscreen&&o->externalError<0))return S_OK;
  try{
   ComPtr<ID3D11Texture2D> back;Check(o->swap->GetBuffer(0,IID_PPV_ARGS(&back)));o->context->CopyResource(back.Get(),result.Get());HRESULT hr=o->swap->Present(o->embedded?0:1,0);
   if(o->offscreen&&FAILED(hr)){o->externalError=hr;return S_OK;}return hr;
  }catch(HRESULT hr){if(o->offscreen){o->externalError=hr;return S_OK;}throw;}
 }catch(HRESULT hr){return hr;}
}
extern "C" __declspec(dllexport) int __cdecl output_cover(void* handle,const unsigned char* pixels,int width,int height){
 auto o=(Output*)handle;if(!o||width<0||height<0||width>8192||height>8192)return E_INVALIDARG;
 try{
  PrepareComposite(o);
  ComPtr<ID3D11Texture2D> image;ComPtr<ID3D11ShaderResourceView> view;
  if(pixels&&width>0&&height>0){D3D11_TEXTURE2D_DESC td={};td.Width=width;td.Height=height;td.MipLevels=td.ArraySize=1;td.Format=DXGI_FORMAT_B8G8R8A8_UNORM;td.SampleDesc.Count=1;td.BindFlags=D3D11_BIND_SHADER_RESOURCE;D3D11_SUBRESOURCE_DATA data={pixels,(UINT)width*4,0};Check(o->device->CreateTexture2D(&td,&data,&image));Check(o->device->CreateShaderResourceView(image.Get(),nullptr,&view));}
  o->coverImage=image;o->coverView=view;o->coverWidth=image?width:0;o->coverHeight=image?height:0;return S_OK;
 }catch(HRESULT hr){return hr;}
}
extern "C" __declspec(dllexport) int __cdecl output_cover_mix(void* handle,float mix){auto o=(Output*)handle;if(!o||!std::isfinite(mix)||mix<0||mix>1)return E_INVALIDARG;o->coverMix=mix;return S_OK;}
extern "C" __declspec(dllexport) int __cdecl output_redraw(void* handle){auto o=(Output*)handle;if(!o||(!o->offscreen&&!IsWindow(o->window)))return E_INVALIDARG;return PresentComposite(o);}
