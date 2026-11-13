// SPDX-License-Identifier: LGPL-3.0-or-later
#define NOMINMAX
#include <windows.h>
#include <mmdeviceapi.h>
#include <audioclient.h>
#include <functiondiscoverykeys_devpkey.h>
#include <ksmedia.h>
#include <wrl/client.h>
#include <vector>
#include <memory>
#include <algorithm>
#include <cmath>
#include "ltc.h"
using Microsoft::WRL::ComPtr;
static void Check(HRESULT h){if(FAILED(h)) throw h;}
struct ComScope { HRESULT hr=CoInitializeEx(nullptr,COINIT_MULTITHREADED); ~ComScope(){if(SUCCEEDED(hr)) CoUninitialize();} };
struct InputDevice { wchar_t id[512]; wchar_t name[256]; };
extern "C" __declspec(dllexport) int __cdecl audio_devices(InputDevice* output,int capacity,int* count) {
    if(!count || capacity<0) return E_INVALIDARG; *count=0;
    ComScope scope; if(FAILED(scope.hr) && scope.hr!=RPC_E_CHANGED_MODE) return scope.hr;
    try {
        ComPtr<IMMDeviceEnumerator> e; Check(CoCreateInstance(__uuidof(MMDeviceEnumerator),nullptr,CLSCTX_ALL,IID_PPV_ARGS(&e)));
        ComPtr<IMMDeviceCollection> ds; Check(e->EnumAudioEndpoints(eCapture,DEVICE_STATE_ACTIVE,&ds));
        UINT n; Check(ds->GetCount(&n)); *count=(int)n;
        for(UINT i=0;i<n && i<(UINT)capacity;++i) {
            ComPtr<IMMDevice> d; Check(ds->Item(i,&d)); LPWSTR id=nullptr; Check(d->GetId(&id));
            InputDevice item={}; wcsncpy_s(item.id,id,_TRUNCATE); CoTaskMemFree(id);
            ComPtr<IPropertyStore> props; Check(d->OpenPropertyStore(STGM_READ,&props));
            PROPVARIANT v; PropVariantInit(&v); Check(props->GetValue(PKEY_Device_FriendlyName,&v));
            if(v.vt==VT_LPWSTR) wcsncpy_s(item.name,v.pwszVal,_TRUNCATE); PropVariantClear(&v);
            if(output) output[i]=item;
        }
        return S_OK;
    } catch(HRESULT h){return h;} catch(...){return E_OUTOFMEMORY;}
}
struct Capture {
    ComPtr<IAudioClient> client; ComPtr<IAudioCaptureClient> capture;
    WAVEFORMATEX* format=nullptr; int channel=0; bool com=false;
    ~Capture(){if(client) client->Stop(); capture.Reset(); client.Reset(); CoTaskMemFree(format); if(com) CoUninitialize();}
};
extern "C" __declspec(dllexport) int __cdecl audio_open(const wchar_t* id,int channel,void** result,int* rate,int* channels) {
    if(!id || !result || !rate || !channels || channel<0) return E_INVALIDARG; *result=nullptr;
    try {
        auto c=std::make_unique<Capture>(); HRESULT init=CoInitializeEx(nullptr,COINIT_MULTITHREADED); Check(init); c->com=true;
        ComPtr<IMMDeviceEnumerator> e; Check(CoCreateInstance(__uuidof(MMDeviceEnumerator),nullptr,CLSCTX_ALL,IID_PPV_ARGS(&e)));
        ComPtr<IMMDevice> d; Check(e->GetDevice(id,&d)); Check(d->Activate(__uuidof(IAudioClient),CLSCTX_ALL,nullptr,(void**)c->client.GetAddressOf()));
        Check(c->client->GetMixFormat(&c->format)); auto f=c->format;
        bool floating=f->wFormatTag==WAVE_FORMAT_IEEE_FLOAT;
        bool pcm=f->wFormatTag==WAVE_FORMAT_PCM;
        if(f->wFormatTag==WAVE_FORMAT_EXTENSIBLE && f->cbSize>=22) {
            auto ex=(WAVEFORMATEXTENSIBLE*)f; floating=ex->SubFormat==KSDATAFORMAT_SUBTYPE_IEEE_FLOAT; pcm=ex->SubFormat==KSDATAFORMAT_SUBTYPE_PCM;
        }
        if(channel>=f->nChannels || f->nSamplesPerSec<8000 || f->nSamplesPerSec>192000 ||
            !((floating && (f->wBitsPerSample==32 || f->wBitsPerSample==64)) || (pcm && (f->wBitsPerSample==16 || f->wBitsPerSample==24 || f->wBitsPerSample==32)))) return AUDCLNT_E_UNSUPPORTED_FORMAT;
        c->channel=channel;
        Check(c->client->Initialize(AUDCLNT_SHAREMODE_SHARED,0,1000000,0,f,nullptr));
        Check(c->client->GetService(IID_PPV_ARGS(&c->capture))); Check(c->client->Start());
        *rate=f->nSamplesPerSec; *channels=f->nChannels; *result=c.release(); return S_OK;
    } catch(HRESULT h){return h;} catch(...){return E_OUTOFMEMORY;}
}
static void Convert(WAVEFORMATEX* f,const BYTE* data,int channel,UINT n,bool silent,float* mono) {
         bool floating=f->wFormatTag==WAVE_FORMAT_IEEE_FLOAT || (f->wFormatTag==WAVE_FORMAT_EXTENSIBLE && ((WAVEFORMATEXTENSIBLE*)f)->SubFormat==KSDATAFORMAT_SUBTYPE_IEEE_FLOAT);
        for(UINT i=0;i<n;++i) {
            float value=0;
            if(!silent) {
                auto p=data+i*f->nBlockAlign+channel*(f->wBitsPerSample/8);
                if(floating) value=f->wBitsPerSample==32?*(float*)p:(float)*(double*)p;
                else if(f->wBitsPerSample==16) value=*(short*)p/32768.f;
                else if(f->wBitsPerSample==32) value=(float)(*(int*)p/2147483648.0);
                else { int v=p[0]|(p[1]<<8)|(p[2]<<16); if(v&0x800000) v|=~0xffffff; value=v/8388608.f; }
            }
            mono[i]=std::isfinite(value)?std::clamp(value,-1.f,1.f):0;
        }

}
extern "C" __declspec(dllexport) int __cdecl audio_convert_test(const BYTE* data,int byteCount,int bits,int floating,int channels,int channel,int count,float* mono) {
    if(!data || !mono || channels<1 || channels>8 || channel<0 || channel>=channels || count<0 ||
       !((floating && (bits==32 || bits==64)) || (!floating && (bits==16 || bits==24 || bits==32))) ||
       (long long)count*channels*(bits/8)>byteCount) return E_INVALIDARG;
    WAVEFORMATEX f={}; f.wFormatTag=floating?WAVE_FORMAT_IEEE_FLOAT:WAVE_FORMAT_PCM;
    f.wBitsPerSample=(WORD)bits; f.nChannels=(WORD)channels; f.nBlockAlign=(WORD)(channels*bits/8);
    Convert(&f,data,channel,count,false,mono); return S_OK;
}
extern "C" __declspec(dllexport) int __cdecl audio_poll(void* handle,float* mono,int capacity,int* count,unsigned long long* position,unsigned long long* qpc,int* flags) {
    auto c=(Capture*)handle; if(!c || !mono || !count || !position || !qpc || !flags) return E_INVALIDARG;
    *count=0; *flags=0;
    try {
        UINT n; Check(c->capture->GetNextPacketSize(&n)); if(!n) return S_OK;
        if(n>(UINT)capacity) return HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER);
        BYTE* data; DWORD fl; Check(c->capture->GetBuffer(&data,&n,&fl,position,qpc));
        Convert(c->format,data,c->channel,n,(fl&AUDCLNT_BUFFERFLAGS_SILENT)!=0,mono);
        Check(c->capture->ReleaseBuffer(n)); *count=n; *flags=(int)fl; return S_OK;
    } catch(HRESULT h){return h;}
}
extern "C" __declspec(dllexport) void __cdecl audio_close(void* handle){delete (Capture*)handle;}
struct TcFrame { int hours,minutes,seconds,frame,drop,reverse; long long start,end; };
extern "C" __declspec(dllexport) void* __cdecl tc_create(int rate,double fps){return ltc_decoder_create((int)(rate/fps),128);}
extern "C" __declspec(dllexport) void __cdecl tc_free(void* d){if(d) ltc_decoder_free((LTCDecoder*)d);}
extern "C" __declspec(dllexport) void __cdecl tc_feed(void* d,float* data,int count,long long position){ltc_decoder_write_float((LTCDecoder*)d,data,count,position);}
extern "C" __declspec(dllexport) int __cdecl tc_read(void* d,TcFrame* out) {
    LTCFrameExt f; if(!ltc_decoder_read((LTCDecoder*)d,&f)) return 0; SMPTETimecode t; ltc_frame_to_time(&t,&f.ltc,0);
    *out={t.hours,t.mins,t.secs,t.frame,(int)f.ltc.dfbit,f.reverse,f.off_start,f.off_end}; return 1;
}
// Synthetic verification fixture through the same feed/read ABI as capture.
extern "C" __declspec(dllexport) int __cdecl tc_generate(double fps,int drop,int hours,int minutes,int seconds,int frame,int frames,float* data,int capacity) {
    LTCEncoder* e=ltc_encoder_create(48000,fps,fps==25?LTC_TV_625_50:fps==24?LTC_TV_FILM_24:LTC_TV_525_60,0); if(!e) return -1;
    SMPTETimecode t={}; t.hours=(unsigned char)hours; t.mins=(unsigned char)minutes; t.secs=(unsigned char)seconds; t.frame=(unsigned char)frame;
    ltc_encoder_set_timecode(e,&t); LTCFrame f; ltc_encoder_get_frame(e,&f); f.dfbit=drop?1:0; ltc_encoder_set_frame(e,&f);
    int at=0;
    for(int i=0;i<frames;++i) { ltc_encoder_encode_frame(e); ltcsnd_sample_t* p; int n=ltc_encoder_get_bufferptr(e,&p,1);
        if(at+n>capacity){ltc_encoder_free(e); return -2;} for(int j=0;j<n;++j) data[at++]=(p[j]-128)/127.f; ltc_encoder_inc_timecode(e); }
    ltc_encoder_free(e); return at;
}


