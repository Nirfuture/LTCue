#define NOMINMAX
#include <windows.h>
#include <d3d11.h>
#include <d3dcompiler.h>
#include <wrl/client.h>
#include <vector>
#include <memory>
#include <cstring>
#include <algorithm>
#include <cmath>
using Microsoft::WRL::ComPtr;
struct Display { int x,y,width,height,primary; wchar_t name[32]; };
static BOOL CALLBACK Collect(HMONITOR monitor,HDC,LPRECT,LPARAM data) {
    MONITORINFOEXW mi={}; mi.cbSize=sizeof(mi);
    if(GetMonitorInfoW(monitor,&mi)) {
        Display d={mi.rcMonitor.left,mi.rcMonitor.top,mi.rcMonitor.right-mi.rcMonitor.left,
            mi.rcMonitor.bottom-mi.rcMonitor.top,(mi.dwFlags&MONITORINFOF_PRIMARY)?1:0,{}};
        wcscpy_s(d.name,mi.szDevice); reinterpret_cast<std::vector<Display>*>(data)->push_back(d);
    }
    return TRUE;
}
static std::vector<Display> Displays() { std::vector<Display> d; EnumDisplayMonitors(nullptr,nullptr,Collect,reinterpret_cast<LPARAM>(&d)); return d; }
extern "C" __declspec(dllexport) int __cdecl output_displays(Display* data,int capacity) {
    auto ds=Displays(); if(data && capacity>0) for(int i=0;i<capacity && i<(int)ds.size();++i) data[i]=ds[i];
    return (int)ds.size();
}
struct Output {
    Display selectedDisplay={}; wchar_t monitorDeviceId[128]={}; bool displayTracked=false,displayInvalidated=false;
    HWND window=nullptr; int width=0,height=0; int externalError=0; bool offscreen=false; bool embedded=false,ownsWindow=true,confirmClose=false,stopRequested=false;
    ComPtr<IDXGISwapChain> monitor;HWND monitorWindow=nullptr;int monitorWidth=0,monitorHeight=0,monitorError=0;
    ComPtr<ID3D11Texture2D> monitorImage;ComPtr<ID3D11RenderTargetView> monitorTarget;
    ComPtr<ID3D11VertexShader> monitorVertex;ComPtr<ID3D11PixelShader> monitorShader;
    ComPtr<ID3D11RasterizerState> monitorRaster;bool monitorBusy=false;ComPtr<ID3D11SamplerState> monitorSampler;ComPtr<ID3D11Query> monitorPending;
    LONGLONG monitorStamp=0;unsigned long long monitorFrames=0;
    bool monitorPresentPending=false;HRESULT monitorPresentResult=S_FALSE;
    unsigned long long monitorRendered=0,monitorRetries=0,monitorOccluded=0,monitorGpuBusy=0;
    ComPtr<ID3D11Device> device; ComPtr<ID3D11DeviceContext> context;
    ComPtr<IDXGISwapChain> swap; ComPtr<ID3D11Texture2D> upload;
    ComPtr<ID3D11Texture2D> compressed,rendered,last;
    ComPtr<ID3D11ShaderResourceView> compressedView;
    ComPtr<ID3D11RenderTargetView> renderedView;
    ComPtr<ID3D11VertexShader> vertex;
    ComPtr<ID3D11PixelShader> hapShader,qShader;
    ComPtr<ID3D11SamplerState> sampler;
    ComPtr<ID3D11RasterizerState> raster;
    ComPtr<ID3D11Texture2D> baseImage,coverImage,compositeImage;
    ComPtr<ID3D11ShaderResourceView> coverView;
    ComPtr<ID3D11RenderTargetView> compositeTarget;
    ComPtr<ID3D11VertexShader> compositeVertex;ComPtr<ID3D11PixelShader> compositeShader;
    ComPtr<ID3D11RasterizerState> compositeRaster;ComPtr<ID3D11SamplerState> compositeSampler;ComPtr<ID3D11Buffer> compositeConstants;
    float coverMix=0;int coverWidth=0,coverHeight=0,compositeWidth=0,compositeHeight=0;
    int sourceWidth=0,sourceHeight=0,sourceKind=0;
    ~Output(){if(context){context->ClearState();context->Flush();}monitor.Reset();swap.Reset();if(context)context->Flush();context.Reset();upload.Reset();device.Reset();if(ownsWindow && window && IsWindow(window))DestroyWindow(window);}
};
static void Check(HRESULT hr){if(FAILED(hr)) throw hr;}
// Never let Windows relocate a live topmost output onto another monitor.
static bool OutputDisplayConnected(Output* o) {
    if(!o->displayTracked)return true;
    auto& d=o->selectedDisplay;RECT bounds={d.x,d.y,d.x+d.width,d.y+d.height};
    HMONITOR monitor=MonitorFromRect(&bounds,MONITOR_DEFAULTTONULL);MONITORINFOEXW info={};info.cbSize=sizeof(info);
    if(!monitor||!GetMonitorInfoW(monitor,&info)||wcscmp(info.szDevice,d.name)||!EqualRect(&info.rcMonitor,&bounds))return false;
    DISPLAY_DEVICEW device={};device.cb=sizeof(device);
    return !o->monitorDeviceId[0]||(EnumDisplayDevicesW(d.name,0,&device,EDD_GET_DEVICE_INTERFACE_NAME)&&!wcscmp(device.DeviceID,o->monitorDeviceId));
}
static void HideDisconnectedOutput(Output* o) {
    if(!o||!o->displayTracked||o->displayInvalidated)return;
    o->displayInvalidated=true;
    o->externalError=HRESULT_FROM_WIN32(ERROR_DEVICE_NOT_CONNECTED);
    if(o->window&&IsWindow(o->window)){
        ShowWindow(o->window,SW_HIDE);
        SetWindowPos(o->window,HWND_NOTOPMOST,0,0,0,0,SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE|SWP_HIDEWINDOW);
    }
}
static LRESULT CALLBACK OutputProc(HWND window,UINT message,WPARAM w,LPARAM l) {
    auto output=(Output*)GetWindowLongPtrW(window,GWLP_USERDATA);
    // Ignore notifications that leave the selected endpoint and geometry unchanged.
    if(message==WM_DISPLAYCHANGE&&output&&output->displayTracked){if(!OutputDisplayConnected(output))HideDisconnectedOutput(output);return 0;}
    if(message==WM_WINDOWPOSCHANGING&&output&&output->displayTracked){
        if(!OutputDisplayConnected(output))HideDisconnectedOutput(output);
        if(output->externalError<0){auto pos=(WINDOWPOS*)l;pos->flags=(pos->flags&~SWP_SHOWWINDOW)|SWP_HIDEWINDOW|SWP_NOACTIVATE;pos->hwndInsertAfter=HWND_NOTOPMOST;}
    }
    if(message==WM_CLOSE || (message==WM_KEYDOWN && w==VK_ESCAPE)) {auto o=(Output*)GetWindowLongPtrW(window,GWLP_USERDATA);if(o&&o->confirmClose)o->stopRequested=true;else DestroyWindow(window); return 0; }
    return DefWindowProcW(window,message,w,l);
}
static int CreateOutput(const Display* selected,void** result,bool probe,bool composited=false) {
    if(!selected || !result) return E_INVALIDARG; *result=nullptr;
    try {
        bool found=false; for(auto& d:Displays()) if(!wcscmp(d.name,selected->name) && d.x==selected->x && d.y==selected->y && d.width==selected->width && d.height==selected->height) found=true;
        if(!found) return HRESULT_FROM_WIN32(ERROR_DEVICE_NOT_CONNECTED);
        auto o=std::make_unique<Output>(); o->width=selected->width; o->height=selected->height;
        WNDCLASSW cls={}; cls.lpfnWndProc=OutputProc; cls.hInstance=GetModuleHandleW(nullptr);
        cls.lpszClassName=L"LtcPlayerOutput"; cls.hbrBackground=(HBRUSH)GetStockObject(BLACK_BRUSH);
        if(!RegisterClassW(&cls) && GetLastError()!=ERROR_CLASS_ALREADY_EXISTS) return HRESULT_FROM_WIN32(GetLastError());
        o->window=CreateWindowExW(WS_EX_NOACTIVATE,cls.lpszClassName,L"LTCue Output",WS_POPUP,
            selected->x,selected->y,probe?640:o->width,probe?360:o->height,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);
        if(!o->window) return HRESULT_FROM_WIN32(GetLastError());
        SetWindowLongPtrW(o->window,GWLP_USERDATA,(LONG_PTR)o.get());
        DXGI_SWAP_CHAIN_DESC sd={}; sd.BufferDesc.Width=o->width; sd.BufferDesc.Height=o->height;
        sd.BufferDesc.Format=DXGI_FORMAT_B8G8R8A8_UNORM; sd.SampleDesc.Count=1;
        sd.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT; sd.BufferCount=2; sd.OutputWindow=o->window;
        sd.Windowed=TRUE; sd.SwapEffect=composited?DXGI_SWAP_EFFECT_DISCARD:DXGI_SWAP_EFFECT_FLIP_DISCARD;
        Check(D3D11CreateDeviceAndSwapChain(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            nullptr,0,D3D11_SDK_VERSION,&sd,&o->swap,&o->device,nullptr,&o->context));
        ComPtr<IDXGIFactory> factory; Check(o->swap->GetParent(IID_PPV_ARGS(&factory)));
        Check(factory->MakeWindowAssociation(o->window,DXGI_MWA_NO_ALT_ENTER));
        D3D11_TEXTURE2D_DESC td={}; td.Width=o->width; td.Height=o->height; td.MipLevels=1; td.ArraySize=1;
        td.Format=sd.BufferDesc.Format; td.SampleDesc.Count=1; td.Usage=D3D11_USAGE_DEFAULT;td.BindFlags=D3D11_BIND_SHADER_RESOURCE;
        Check(o->device->CreateTexture2D(&td,nullptr,&o->upload));
        ShowWindow(o->window,SW_SHOWNOACTIVATE);
        SetWindowPos(o->window,probe?HWND_NOTOPMOST:HWND_TOPMOST,selected->x,selected->y,probe?640:o->width,probe?360:o->height,SWP_NOACTIVATE|SWP_SHOWWINDOW);
        UpdateWindow(o->window); *result=o.release(); return S_OK;
    } catch(HRESULT hr){return hr;} catch(...){return E_OUTOFMEMORY;}
}
extern "C" __declspec(dllexport) int __cdecl output_create(const Display* selected,void** result){return CreateOutput(selected,result,false);}
// Compatibility path selected explicitly in application settings.
extern "C" __declspec(dllexport) int __cdecl output_create_composited(const Display* selected,void** result,int probe){return CreateOutput(selected,result,probe!=0,true);}
extern "C" __declspec(dllexport) int __cdecl output_presentation_mode(void* handle){
 auto o=(Output*)handle;if(!o||!o->swap)return E_INVALIDARG;DXGI_SWAP_CHAIN_DESC sd={};HRESULT hr=o->swap->GetDesc(&sd);if(FAILED(hr))return hr;
 return sd.SwapEffect==DXGI_SWAP_EFFECT_DISCARD?1:sd.SwapEffect==DXGI_SWAP_EFFECT_FLIP_DISCARD?0:E_FAIL;
}
// Verification-only geometry: preserve full-resolution rendering without covering a live output.
extern "C" __declspec(dllexport) int __cdecl output_create_probe(const Display* selected,void** result){return CreateOutput(selected,result,true);}
#include "Composite.inc"
extern "C" __declspec(dllexport) int __cdecl output_frame(void* handle,const unsigned char* pixels,int length) {
    auto o=static_cast<Output*>(handle); if(!o || !pixels || length!=o->width*o->height*4) return E_INVALIDARG;
    if(!o->offscreen && (!IsWindow(o->window) || (!o->embedded && !o->confirmClose && (GetAsyncKeyState(VK_ESCAPE)&0x8000)))) return HRESULT_FROM_WIN32(ERROR_CANCELLED);
    try {
        o->context->UpdateSubresource(o->upload.Get(),0,nullptr,pixels,o->width*4,0);
        o->baseImage=o->upload;return PresentComposite(o);
    } catch(HRESULT hr){return hr;}
}
// Verification-only readback of upload texture. Presentation is counted separately.
extern "C" __declspec(dllexport) int __cdecl output_check(void* handle,const unsigned char* pixels,int length) {
    auto o=static_cast<Output*>(handle); if(!o || !pixels || length!=o->width*o->height*4) return E_INVALIDARG;
    try {
        if(!o->last)return E_FAIL;
        D3D11_TEXTURE2D_DESC td; o->last->GetDesc(&td); td.Usage=D3D11_USAGE_STAGING; td.CPUAccessFlags=D3D11_CPU_ACCESS_READ;td.BindFlags=0;
        ComPtr<ID3D11Texture2D> staging; Check(o->device->CreateTexture2D(&td,nullptr,&staging));
        o->context->CopyResource(staging.Get(),o->last.Get()); D3D11_MAPPED_SUBRESOURCE m;
        Check(o->context->Map(staging.Get(),0,D3D11_MAP_READ,0,&m)); bool ok=true;
        for(int y=0;y<o->height;++y) if(memcmp((char*)m.pData+y*m.RowPitch,pixels+y*o->width*4,o->width*4)) ok=false;
        o->context->Unmap(staging.Get(),0); return ok?S_OK:HRESULT_FROM_WIN32(ERROR_CRC);
    } catch(HRESULT hr){return hr;}
}
extern "C" __declspec(dllexport) int __cdecl output_bounds(void* handle,RECT* rect) {
    auto o=static_cast<Output*>(handle); return o && rect && GetWindowRect(o->window,rect)?S_OK:E_FAIL;
}
extern "C" __declspec(dllexport) void __cdecl output_destroy(void* handle){delete static_cast<Output*>(handle);}
#include "HapGpu.inc"



