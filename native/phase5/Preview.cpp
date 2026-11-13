// Embedded preview host, sharing the output renderer and GPU composition pipeline.
#include "Player.cpp"
// A STATIC control can repaint its background over a DXGI presentation.
// This child is exclusively owned by the video renderer; validate paint without GDI drawing.
static LRESULT CALLBACK PreviewWindowProc(HWND window,UINT message,WPARAM w,LPARAM l){
 if(message==WM_ERASEBKGND)return 1;
 if(message==WM_PAINT){PAINTSTRUCT paint;BeginPaint(window,&paint);EndPaint(window,&paint);return 0;}
 if(message==WM_SIZE){if(HWND child=GetWindow(window,GW_CHILD))SetWindowPos(child,nullptr,0,0,LOWORD(l),HIWORD(l),SWP_NOZORDER|SWP_NOACTIVATE);}
 return DefWindowProcW(window,message,w,l);
}
static HWND CreatePreviewChild(HWND parent){
 WNDCLASSW cls={};cls.lpfnWndProc=PreviewWindowProc;cls.hInstance=GetModuleHandleW(nullptr);cls.lpszClassName=L"LtcPlayerPreviewSurface";
 if(!RegisterClassW(&cls)&&GetLastError()!=ERROR_CLASS_ALREADY_EXISTS)return nullptr;
 RECT r={};GetClientRect(parent,&r);
 return CreateWindowExW(0,cls.lpszClassName,L"",WS_CHILD|WS_VISIBLE|WS_CLIPSIBLINGS|WS_CLIPCHILDREN,0,0,std::max(1L,r.right),std::max(1L,r.bottom),parent,nullptr,GetModuleHandleW(nullptr),nullptr);
}
extern "C" __declspec(dllexport) int __cdecl preview_child_create(HWND parent,HWND* window){if(!IsWindow(parent)||!window)return E_INVALIDARG;*window=CreatePreviewChild(parent);return *window?S_OK:HRESULT_FROM_WIN32(GetLastError());}
extern "C" __declspec(dllexport) int __cdecl preview_create(HWND parent,void** result,HWND* window) {
 if(!parent||!result||!window)return E_INVALIDARG;*result=nullptr;*window=nullptr;
 try {
  auto o=std::make_unique<Output>();o->width=640;o->height=360;o->embedded=true;o->ownsWindow=false;
  o->window=CreatePreviewChild(parent);
  if(!o->window)return HRESULT_FROM_WIN32(GetLastError());
  DXGI_SWAP_CHAIN_DESC sd={};sd.BufferDesc.Width=o->width;sd.BufferDesc.Height=o->height;
  sd.BufferDesc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;sd.SampleDesc.Count=1;
  sd.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;sd.BufferCount=2;sd.OutputWindow=o->window;
  sd.Windowed=TRUE;sd.SwapEffect=DXGI_SWAP_EFFECT_FLIP_DISCARD;
  Check(D3D11CreateDeviceAndSwapChain(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,
   nullptr,0,D3D11_SDK_VERSION,&sd,&o->swap,&o->device,nullptr,&o->context));
  ComPtr<IDXGIFactory> f;Check(o->swap->GetParent(IID_PPV_ARGS(&f)));Check(f->MakeWindowAssociation(o->window,DXGI_MWA_NO_ALT_ENTER));
  D3D11_TEXTURE2D_DESC td={};td.Width=o->width;td.Height=o->height;td.MipLevels=td.ArraySize=1;
  td.Format=sd.BufferDesc.Format;td.SampleDesc.Count=1;td.BindFlags=D3D11_BIND_SHADER_RESOURCE;Check(o->device->CreateTexture2D(&td,nullptr,&o->upload));
  *window=o->window;*result=o.release();return S_OK;
 }catch(HRESULT hr){return hr;}catch(...){return E_OUTOFMEMORY;}
}
extern "C" __declspec(dllexport) int __cdecl preview_host_create(HWND parent,void** result,HWND* container,HWND* window){
 if(!result||!container||!window)return E_INVALIDARG;*container=CreatePreviewChild(parent);if(!*container)return HRESULT_FROM_WIN32(GetLastError());
 int hr=preview_create(*container,result,window);if(FAILED(hr)){DestroyWindow(*container);*container=nullptr;}return hr;
}
extern "C" __declspec(dllexport) int __cdecl preview_resume(HWND existing,void** result) {
 if(!IsWindow(existing)||!result)return E_INVALIDARG;*result=nullptr;
 try {
  auto o=std::make_unique<Output>();o->width=640;o->height=360;o->embedded=true;o->ownsWindow=false;
  o->window=existing;
  DXGI_SWAP_CHAIN_DESC sd={};sd.BufferDesc.Width=o->width;sd.BufferDesc.Height=o->height;
  sd.BufferDesc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;sd.SampleDesc.Count=1;
  sd.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;sd.BufferCount=2;sd.OutputWindow=o->window;
  sd.Windowed=TRUE;sd.SwapEffect=DXGI_SWAP_EFFECT_FLIP_DISCARD;
  Check(D3D11CreateDeviceAndSwapChain(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,
   nullptr,0,D3D11_SDK_VERSION,&sd,&o->swap,&o->device,nullptr,&o->context));
  ComPtr<IDXGIFactory> f;Check(o->swap->GetParent(IID_PPV_ARGS(&f)));Check(f->MakeWindowAssociation(o->window,DXGI_MWA_NO_ALT_ENTER));
  D3D11_TEXTURE2D_DESC td={};td.Width=o->width;td.Height=o->height;td.MipLevels=td.ArraySize=1;
  td.Format=sd.BufferDesc.Format;td.SampleDesc.Count=1;td.BindFlags=D3D11_BIND_SHADER_RESOURCE;Check(o->device->CreateTexture2D(&td,nullptr,&o->upload));
  *result=o.release();return S_OK;
 }catch(HRESULT hr){return hr;}catch(...){return E_OUTOFMEMORY;}
}
extern "C" __declspec(dllexport) int __cdecl preview_size(void* handle,int* width,int* height) {
 auto o=(Output*)handle;RECT r;if(!o||!width||!height||!GetClientRect(o->window,&r))return E_INVALIDARG;
 int w=std::max(1L,r.right),h=std::max(1L,r.bottom);
 try {
  if(w!=o->width||h!=o->height) {
   o->context->ClearState();o->context->Flush();o->last.Reset();o->baseImage.Reset();o->compositeTarget.Reset();o->compositeImage.Reset();o->upload.Reset();o->renderedView.Reset();o->rendered.Reset();
   o->vertex.Reset();o->hapShader.Reset();o->qShader.Reset();o->sampler.Reset();o->raster.Reset();
   Check(o->swap->ResizeBuffers(0,w,h,DXGI_FORMAT_UNKNOWN,0));
   D3D11_TEXTURE2D_DESC td={};td.Width=w;td.Height=h;td.MipLevels=td.ArraySize=1;
   td.Format=DXGI_FORMAT_B8G8R8A8_UNORM;td.SampleDesc.Count=1;td.BindFlags=D3D11_BIND_SHADER_RESOURCE;
   Check(o->device->CreateTexture2D(&td,nullptr,&o->upload));o->width=w;o->height=h;
  }
  *width=o->width;*height=o->height;return S_OK;
 }catch(HRESULT hr){return hr;}
}

#include "Monitor.inc"

extern "C" __declspec(dllexport) void __cdecl preview_window_destroy(HWND window){if(IsWindow(window))DestroyWindow(window);}


#include "Canvas.inc"
