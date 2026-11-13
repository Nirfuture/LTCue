// SPDX-License-Identifier: MIT
#include <algorithm>
#include <cmath>
extern "C" __declspec(dllexport) int __cdecl monitor_attach(void* handle,HWND target) {
 auto o=(Output*)handle;if(!o||!IsWindow(target))return E_INVALIDARG;
 try {
  o->monitor.Reset();o->monitorImage.Reset();o->monitorTarget.Reset();o->monitorWindow=target;
  RECT r;GetClientRect(target,&r);o->monitorWidth=std::max(1,std::min((int)r.right,o->offscreen?o->width/2:(int)r.right));o->monitorHeight=std::max(1,std::min((int)r.bottom,o->offscreen?o->height/2:(int)r.bottom));
  ComPtr<IDXGIDevice> dxgi;Check(o->device.As(&dxgi));ComPtr<IDXGIAdapter> adapter;Check(dxgi->GetAdapter(&adapter));ComPtr<IDXGIFactory> factory;Check(adapter->GetParent(IID_PPV_ARGS(&factory)));
  DXGI_SWAP_CHAIN_DESC sd={};sd.BufferDesc.Width=o->monitorWidth;sd.BufferDesc.Height=o->monitorHeight;sd.BufferDesc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;
  sd.SampleDesc.Count=1;sd.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;sd.BufferCount=2;sd.OutputWindow=target;sd.Windowed=TRUE;sd.SwapEffect=DXGI_SWAP_EFFECT_DISCARD;
  Check(factory->CreateSwapChain(o->device.Get(),&sd,&o->monitor));Check(factory->MakeWindowAssociation(target,DXGI_MWA_NO_ALT_ENTER));
  const char* shader=R"(Texture2D image:register(t0);SamplerState smp:register(s0);struct V{float4 p:SV_POSITION;float2 uv:TEXCOORD0;};
V VS(uint id:SV_VertexID){V v;v.uv=float2((id<<1)&2,id&2);v.p=float4(v.uv.x*2-1,1-v.uv.y*2,0,1);return v;}
float4 PS(V v):SV_TARGET{return float4(image.Sample(smp,v.uv).rgb,1);})";
  ComPtr<ID3DBlob> vs,ps,err;Check(D3DCompile(shader,strlen(shader),nullptr,nullptr,nullptr,"VS","vs_4_0",0,0,&vs,&err));Check(D3DCompile(shader,strlen(shader),nullptr,nullptr,nullptr,"PS","ps_4_0",0,0,&ps,&err));
  Check(o->device->CreateVertexShader(vs->GetBufferPointer(),vs->GetBufferSize(),nullptr,o->monitorVertex.ReleaseAndGetAddressOf()));Check(o->device->CreatePixelShader(ps->GetBufferPointer(),ps->GetBufferSize(),nullptr,o->monitorShader.ReleaseAndGetAddressOf()));
  D3D11_TEXTURE2D_DESC td={};td.Width=o->monitorWidth;td.Height=o->monitorHeight;td.MipLevels=td.ArraySize=1;td.Format=sd.BufferDesc.Format;td.SampleDesc.Count=1;td.BindFlags=D3D11_BIND_RENDER_TARGET;
  Check(o->device->CreateTexture2D(&td,nullptr,&o->monitorImage));Check(o->device->CreateRenderTargetView(o->monitorImage.Get(),nullptr,&o->monitorTarget));
  D3D11_SAMPLER_DESC samp={};samp.Filter=D3D11_FILTER_MIN_MAG_MIP_LINEAR;samp.AddressU=samp.AddressV=samp.AddressW=D3D11_TEXTURE_ADDRESS_CLAMP;samp.MaxLOD=D3D11_FLOAT32_MAX;Check(o->device->CreateSamplerState(&samp,o->monitorSampler.ReleaseAndGetAddressOf()));
  D3D11_RASTERIZER_DESC rd={};rd.FillMode=D3D11_FILL_SOLID;rd.CullMode=D3D11_CULL_NONE;rd.DepthClipEnable=TRUE;Check(o->device->CreateRasterizerState(&rd,o->monitorRaster.ReleaseAndGetAddressOf()));
  D3D11_QUERY_DESC q={D3D11_QUERY_EVENT,0};Check(o->device->CreateQuery(&q,o->monitorPending.ReleaseAndGetAddressOf()));o->monitorBusy=false;o->monitorPresentPending=false;o->monitorStamp=0;o->monitorError=0;
  o->monitorPresentResult=S_FALSE;o->monitorFrames=o->monitorRendered=o->monitorRetries=o->monitorOccluded=o->monitorGpuBusy=0;return S_OK;
 }catch(HRESULT hr){o->monitorError=hr;return hr;}catch(...){return E_OUTOFMEMORY;}
}
extern "C" __declspec(dllexport) int __cdecl monitor_update(void* handle,int force) {
 auto o=(Output*)handle;if(!o||!o->monitor||!o->last||o->monitorError)return S_FALSE;
 LARGE_INTEGER stamp,freq;QueryPerformanceCounter(&stamp);QueryPerformanceFrequency(&freq);

 if(o->monitorBusy){BOOL done=FALSE;auto hr=o->context->GetData(o->monitorPending.Get(),&done,sizeof(done),D3D11_ASYNC_GETDATA_DONOTFLUSH);if(FAILED(hr)){o->monitorError=hr;return hr;}if(hr!=S_OK||!done){++o->monitorGpuBusy;return S_FALSE;}o->monitorBusy=false;}
 try {
  // Keep at most one pending back buffer. Retry its presentation before writing
  // another frame, including when production is holding its last image.
  if(o->monitorPresentPending){
   HRESULT hr=o->monitor->Present(0,DXGI_PRESENT_DO_NOT_WAIT);o->monitorPresentResult=hr;if(!o->offscreen)o->monitorStamp=stamp.QuadPart;
   if(hr==DXGI_ERROR_WAS_STILL_DRAWING){++o->monitorRetries;return S_FALSE;}
   if(hr==DXGI_STATUS_OCCLUDED){++o->monitorOccluded;return S_FALSE;}
   Check(hr);if(hr!=S_OK)return S_FALSE;
   o->monitorPresentPending=false;++o->monitorFrames;return S_OK;
  }
  const auto period=freq.QuadPart/(o->offscreen?30:120);
  if(!force&&stamp.QuadPart-o->monitorStamp<period)return S_FALSE;
  // Offscreen preview cadence is measured from render admission, not delayed
  // Present completion. Retain phase across polling jitter without catch-up bursts.
  if(o->offscreen)o->monitorStamp=(!force&&o->monitorStamp&&stamp.QuadPart-o->monitorStamp<=period*3)?o->monitorStamp+period:stamp.QuadPart;
  RECT rect={};if(!GetClientRect(o->monitorWindow,&rect))return S_FALSE;
  int width=std::max(1,std::min((int)rect.right,o->offscreen?o->width/2:(int)rect.right)),height=std::max(1,std::min((int)rect.bottom,o->offscreen?o->height/2:(int)rect.bottom));
  if(width!=o->monitorWidth||height!=o->monitorHeight){
   o->context->OMSetRenderTargets(0,nullptr,nullptr);o->monitorTarget.Reset();o->monitorImage.Reset();
   Check(o->monitor->ResizeBuffers(0,width,height,DXGI_FORMAT_UNKNOWN,0));
   D3D11_TEXTURE2D_DESC td={};td.Width=width;td.Height=height;td.MipLevels=td.ArraySize=1;
   td.Format=DXGI_FORMAT_B8G8R8A8_UNORM;td.SampleDesc.Count=1;td.BindFlags=D3D11_BIND_RENDER_TARGET;
   Check(o->device->CreateTexture2D(&td,nullptr,&o->monitorImage));Check(o->device->CreateRenderTargetView(o->monitorImage.Get(),nullptr,&o->monitorTarget));
   o->monitorWidth=width;o->monitorHeight=height;
  }
  ComPtr<ID3D11ShaderResourceView> source;Check(o->device->CreateShaderResourceView(o->last.Get(),nullptr,&source));
  const float black[4]={0,0,0,1};o->context->ClearRenderTargetView(o->monitorTarget.Get(),black);
  float scale=std::min(o->monitorWidth/(float)o->width,o->monitorHeight/(float)o->height);
  float innerWidth=std::round(o->width*scale),innerHeight=std::round(o->height*scale);
  // The offscreen canvas already includes the output's letterboxing. Its host
  // has the same aspect ratio; a second fit adds a one-pixel black edge after
  // HWND/DPI integer rounding. Map the entire canvas to the entire preview.
  D3D11_VIEWPORT vp=o->offscreen?D3D11_VIEWPORT{0,0,(float)o->monitorWidth,(float)o->monitorHeight,0,1}:D3D11_VIEWPORT{std::floor((o->monitorWidth-innerWidth)/2),std::floor((o->monitorHeight-innerHeight)/2),innerWidth,innerHeight,0,1};o->context->RSSetViewports(1,&vp);o->context->RSSetState(o->monitorRaster.Get());
  ID3D11RenderTargetView* rt=o->monitorTarget.Get();o->context->OMSetRenderTargets(1,&rt,nullptr);o->context->IASetInputLayout(nullptr);o->context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
  o->context->VSSetShader(o->monitorVertex.Get(),nullptr,0);o->context->PSSetShader(o->monitorShader.Get(),nullptr,0);auto view=source.Get();o->context->PSSetShaderResources(0,1,&view);auto samp=o->monitorSampler.Get();o->context->PSSetSamplers(0,1,&samp);o->context->Draw(3,0);
  view=nullptr;o->context->PSSetShaderResources(0,1,&view);o->context->OMSetRenderTargets(0,nullptr,nullptr);
  ComPtr<ID3D11Texture2D> back;Check(o->monitor->GetBuffer(0,IID_PPV_ARGS(&back)));o->context->CopyResource(back.Get(),o->monitorImage.Get());o->context->End(o->monitorPending.Get());o->monitorBusy=true;
  // Submit even when the production output is held and its Present no longer flushes.
  // Flush submits commands; completion remains polled without a GPU wait.
  o->context->Flush();
  o->monitorPresentPending=true;++o->monitorRendered;
  // Do not queue a Present behind an unfinished copy on this swap chain.
  // The next control-loop tick polls completion and presents without waiting.
  return S_FALSE;
 }catch(HRESULT hr){o->monitorError=hr;return hr;}
}
extern "C" __declspec(dllexport) int __cdecl monitor_readback(void* handle,unsigned char* pixels,int length) {
 auto o=(Output*)handle;if(!o||!o->monitorImage||length!=o->monitorWidth*o->monitorHeight*4)return E_INVALIDARG;
 try{D3D11_TEXTURE2D_DESC td;o->monitorImage->GetDesc(&td);td.BindFlags=0;td.Usage=D3D11_USAGE_STAGING;td.CPUAccessFlags=D3D11_CPU_ACCESS_READ;ComPtr<ID3D11Texture2D> staging;Check(o->device->CreateTexture2D(&td,nullptr,&staging));o->context->CopyResource(staging.Get(),o->monitorImage.Get());D3D11_MAPPED_SUBRESOURCE m;Check(o->context->Map(staging.Get(),0,D3D11_MAP_READ,0,&m));for(int y=0;y<o->monitorHeight;y++)memcpy(pixels+y*o->monitorWidth*4,(char*)m.pData+y*m.RowPitch,o->monitorWidth*4);o->context->Unmap(staging.Get(),0);return S_OK;}catch(HRESULT hr){return hr;}
}
extern "C" __declspec(dllexport) int __cdecl monitor_stats(void* h,int* w,int* height,unsigned long long* frames){auto o=(Output*)h;if(!o)return E_INVALIDARG;*w=o->monitorWidth;*height=o->monitorHeight;*frames=o->monitorFrames;return o->monitorError;}
extern "C" __declspec(dllexport) int __cdecl monitor_diagnostics(void* h,unsigned long long* rendered,unsigned long long* retries,unsigned long long* occluded,unsigned long long* busy,int* presentResult){auto o=(Output*)h;if(!o||!rendered||!retries||!occluded||!busy||!presentResult)return E_INVALIDARG;*rendered=o->monitorRendered;*retries=o->monitorRetries;*occluded=o->monitorOccluded;*busy=o->monitorGpuBusy;*presentResult=o->monitorPresentResult;return o->monitorError;}
extern "C" __declspec(dllexport) int __cdecl output_stop_request(void* h,int enable){auto o=(Output*)h;if(!o)return 0;o->confirmClose=enable!=0;bool requested=o->stopRequested;o->stopRequested=false;return requested?1:0;}
extern "C" __declspec(dllexport) int __cdecl output_stop_test(void* h,int escape){auto o=(Output*)h;if(!o)return E_INVALIDARG;SendMessageW(o->window,escape?WM_KEYDOWN:WM_CLOSE,escape?VK_ESCAPE:0,0);return IsWindow(o->window)?S_OK:E_FAIL;}


