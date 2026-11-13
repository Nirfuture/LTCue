// SPDX-License-Identifier: MIT
#define NOMINMAX
#include <windows.h>
#include <mmdeviceapi.h>
#include <audioclient.h>
#include <wrl/client.h>
#include <memory>
#include <algorithm>
#include <cmath>
#include <atomic>
#include <string>
#include <functiondiscoverykeys_devpkey.h>
using Microsoft::WRL::ComPtr;
static std::atomic<int> activeAudio{0};
struct PreviewAudio {
 bool com=false,started=false,counted=false;UINT32 capacity=0;UINT64 written=0;float peak=0;
 ComPtr<IMMDeviceEnumerator> enumerator;ComPtr<IMMDevice> endpoint;
 DWORD owner=GetCurrentThreadId();std::wstring deviceId;float rateRatio=1;
 ComPtr<IAudioClock> clock;ComPtr<IAudioClockAdjustment> adjustment;ComPtr<IAudioStreamVolume> streamVolume;
 ComPtr<IAudioClient> client;ComPtr<IAudioRenderClient> render;
 ~PreviewAudio(){if(client){client->Stop();client->Reset();}streamVolume.Reset();adjustment.Reset();clock.Reset();render.Reset();client.Reset();endpoint.Reset();enumerator.Reset();if(counted)--activeAudio;if(com)CoUninitialize();}
};
static void AudioCheck(HRESULT hr){if(FAILED(hr))throw hr;}
static int OpenPreviewAudio(const wchar_t* deviceId,bool adjustable,void** result) {
 if(!result)return E_INVALIDARG;*result=nullptr;
 try {
  auto a=std::make_unique<PreviewAudio>();AudioCheck(CoInitializeEx(nullptr,COINIT_MULTITHREADED));a->com=true;
  AudioCheck(CoCreateInstance(__uuidof(MMDeviceEnumerator),nullptr,CLSCTX_ALL,IID_PPV_ARGS(&a->enumerator)));
  if(deviceId&&deviceId[0])AudioCheck(a->enumerator->GetDevice(deviceId,&a->endpoint));
  else AudioCheck(a->enumerator->GetDefaultAudioEndpoint(eRender,eMultimedia,&a->endpoint));
  ComPtr<IMMEndpoint> ep;AudioCheck(a->endpoint.As(&ep));EDataFlow flow;AudioCheck(ep->GetDataFlow(&flow));if(flow!=eRender)return AUDCLNT_E_WRONG_ENDPOINT_TYPE;
  LPWSTR actualId=nullptr;AudioCheck(a->endpoint->GetId(&actualId));a->deviceId=actualId;CoTaskMemFree(actualId);
  AudioCheck(a->endpoint->Activate(__uuidof(IAudioClient),CLSCTX_ALL,nullptr,(void**)&a->client));
  WAVEFORMATEX f={WAVE_FORMAT_IEEE_FLOAT,2,48000,48000*8,8,32,0};GUID session;AudioCheck(CoCreateGuid(&session));
  AudioCheck(a->client->Initialize(AUDCLNT_SHAREMODE_SHARED,AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM|AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY|(adjustable?AUDCLNT_STREAMFLAGS_RATEADJUST:0),
   1000000,0,&f,&session));
  AudioCheck(a->client->GetBufferSize(&a->capacity));AudioCheck(a->client->GetService(IID_PPV_ARGS(&a->render)));
  if(adjustable){
   AudioCheck(a->client->GetService(IID_PPV_ARGS(&a->clock)));AudioCheck(a->client->GetService(IID_PPV_ARGS(&a->adjustment)));AudioCheck(a->client->GetService(IID_PPV_ARGS(&a->streamVolume)));
  }
  a->counted=true;++activeAudio;*result=a.release();return S_OK;
 }catch(HRESULT hr){return hr;}catch(...){return E_OUTOFMEMORY;}
}
extern "C" __declspec(dllexport) int __cdecl pa_open(void** result){return OpenPreviewAudio(nullptr,false,result);}
extern "C" __declspec(dllexport) int __cdecl pa_open_device(const wchar_t* id,void** result){return OpenPreviewAudio(id,true,result);}
extern "C" __declspec(dllexport) int __cdecl pa_write(void* handle,const float* samples,int offsetFrames,int frames,float gain,int* written) {
 auto a=(PreviewAudio*)handle;if(!a||!samples||!written||offsetFrames<0||frames<0||!std::isfinite(gain)||gain<0||gain>1)return E_INVALIDARG;*written=0;
 try {
  UINT32 padding;AudioCheck(a->client->GetCurrentPadding(&padding));UINT32 count=std::min((UINT32)frames,a->capacity-padding);
  if(!count)return S_OK;BYTE* data;AudioCheck(a->render->GetBuffer(count,&data));float peak=0;
  for(UINT32 i=0;i<count*2;++i){float v=samples[offsetFrames*2+i];v=std::isfinite(v)?std::clamp(v*gain,-1.f,1.f):0.f;((float*)data)[i]=v;peak=std::max(peak,std::abs(v));}
  AudioCheck(a->render->ReleaseBuffer(count,0));a->written+=count;a->peak=peak;*written=(int)count;return S_OK;
 }catch(HRESULT hr){return hr;}
}
extern "C" __declspec(dllexport) int __cdecl pa_run(void* handle,int running) {
 auto a=(PreviewAudio*)handle;if(!a)return E_INVALIDARG;
 HRESULT hr=S_OK;if(running&&!a->started){hr=a->client->Start();if(SUCCEEDED(hr))a->started=true;}
 else if(!running&&a->started){hr=a->client->Stop();if(SUCCEEDED(hr))a->started=false;}
 return FAILED(hr)?hr:S_OK;
}
extern "C" __declspec(dllexport) int __cdecl pa_stats(void* handle,UINT64* written,UINT64* consumed,float* peak) {
 auto a=(PreviewAudio*)handle;if(!a||!written||!consumed||!peak)return E_INVALIDARG;
 UINT32 padding;HRESULT hr=a->client->GetCurrentPadding(&padding);if(FAILED(hr))return hr;
 *written=a->written;*consumed=a->written>=padding?a->written-padding:0;*peak=a->peak;return S_OK;
}
extern "C" __declspec(dllexport) void __cdecl pa_close(void* handle){delete (PreviewAudio*)handle;}
extern "C" __declspec(dllexport) int __cdecl pa_active(){return activeAudio.load();}


// Additive API used by the new shared-playhead audio path. Legacy ABI stays unchanged.
struct OutputAudioDevice {wchar_t id[512];wchar_t name[256];int isDefault;};
struct AudioClockSnapshot {
 UINT64 written,consumed,position,frequency,qpc;
 UINT32 padding,capacity;REFERENCE_TIME latency;
 int running;float rateRatio,peak;int clockStatus;
};
struct AudioComScope {
 HRESULT result=CoInitializeEx(nullptr,COINIT_MULTITHREADED);
 ~AudioComScope(){if(SUCCEEDED(result))CoUninitialize();}
};
extern "C" __declspec(dllexport) int __cdecl pa_devices(OutputAudioDevice* devices,int capacity,int* count){
 if(!count||capacity<0)return E_INVALIDARG;*count=0;
 AudioComScope com;if(FAILED(com.result)&&com.result!=RPC_E_CHANGED_MODE)return com.result;
 try{
  ComPtr<IMMDeviceEnumerator> enumerator;AudioCheck(CoCreateInstance(__uuidof(MMDeviceEnumerator),nullptr,CLSCTX_ALL,IID_PPV_ARGS(&enumerator)));
  std::wstring defaultId;ComPtr<IMMDevice> defaultDevice;
  if(SUCCEEDED(enumerator->GetDefaultAudioEndpoint(eRender,eMultimedia,&defaultDevice))){LPWSTR id=nullptr;AudioCheck(defaultDevice->GetId(&id));defaultId=id;CoTaskMemFree(id);}
  ComPtr<IMMDeviceCollection> collection;AudioCheck(enumerator->EnumAudioEndpoints(eRender,DEVICE_STATE_ACTIVE,&collection));UINT n;AudioCheck(collection->GetCount(&n));*count=(int)n;
  for(UINT i=0;i<n&&i<(UINT)capacity;i++){
   ComPtr<IMMDevice> device;AudioCheck(collection->Item(i,&device));LPWSTR id=nullptr;AudioCheck(device->GetId(&id));
   OutputAudioDevice item={};wcsncpy_s(item.id,id,_TRUNCATE);item.isDefault=defaultId==id?1:0;CoTaskMemFree(id);
   ComPtr<IPropertyStore> store;AudioCheck(device->OpenPropertyStore(STGM_READ,&store));PROPVARIANT value;PropVariantInit(&value);
   HRESULT hr=store->GetValue(PKEY_Device_FriendlyName,&value);if(SUCCEEDED(hr)&&value.vt==VT_LPWSTR)wcsncpy_s(item.name,value.pwszVal,_TRUNCATE);PropVariantClear(&value);AudioCheck(hr);
   if(devices)devices[i]=item;
  }
  return S_OK;
 }catch(HRESULT hr){return hr;}catch(...){return E_OUTOFMEMORY;}
}
static int AudioOwner(PreviewAudio* audio){return !audio?E_INVALIDARG:audio->owner!=GetCurrentThreadId()?RPC_E_WRONG_THREAD:S_OK;}
extern "C" __declspec(dllexport) int __cdecl pa_device_id(void* handle,wchar_t* id,int capacity){
 auto a=(PreviewAudio*)handle;int hr=AudioOwner(a);if(FAILED(hr))return hr;if(!id||capacity<1)return E_INVALIDARG;
 if(a->deviceId.size()>=(size_t)capacity)return HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER);wcscpy_s(id,capacity,a->deviceId.c_str());return S_OK;
}
extern "C" __declspec(dllexport) int __cdecl pa_clock_snapshot(void* handle,AudioClockSnapshot* snapshot){
 auto a=(PreviewAudio*)handle;int hr=AudioOwner(a);if(FAILED(hr))return hr;if(!snapshot||!a->clock)return E_INVALIDARG;
 try{
  AudioClockSnapshot s={};AudioCheck(a->client->GetCurrentPadding(&s.padding));AudioCheck(a->clock->GetFrequency(&s.frequency));
  HRESULT positionResult=a->clock->GetPosition(&s.position,&s.qpc);AudioCheck(positionResult);s.clockStatus=positionResult;
  AudioCheck(a->client->GetStreamLatency(&s.latency));s.written=a->written;s.consumed=a->written>=s.padding?a->written-s.padding:0;s.capacity=a->capacity;s.running=a->started?1:0;s.rateRatio=a->rateRatio;s.peak=a->peak;*snapshot=s;return S_OK;
 }catch(HRESULT h){return h;}
}
extern "C" __declspec(dllexport) int __cdecl pa_reset(void* handle){
 auto a=(PreviewAudio*)handle;int hr=AudioOwner(a);if(FAILED(hr))return hr;
 try{AudioCheck(a->client->Stop());a->started=false;AudioCheck(a->client->Reset());
  if(a->adjustment)AudioCheck(a->adjustment->SetSampleRate(48000));a->rateRatio=1;a->written=0;a->peak=0;return S_OK;
 }catch(HRESULT h){return h;}
}
extern "C" __declspec(dllexport) int __cdecl pa_rate_ratio(void* handle,float ratio){
 auto a=(PreviewAudio*)handle;int hr=AudioOwner(a);if(FAILED(hr))return hr;if(!a->adjustment||!std::isfinite(ratio)||ratio<.98f||ratio>1.02f)return E_INVALIDARG;
 HRESULT result=a->adjustment->SetSampleRate(48000*ratio);if(SUCCEEDED(result))a->rateRatio=ratio;return result;
}
extern "C" __declspec(dllexport) int __cdecl pa_stream_gain(void* handle,float gain){
 auto a=(PreviewAudio*)handle;int hr=AudioOwner(a);if(FAILED(hr))return hr;if(!a->streamVolume||!std::isfinite(gain)||gain<0||gain>1)return E_INVALIDARG;
 float volumes[2]={gain,gain};return a->streamVolume->SetAllVolumes(2,volumes);
}
