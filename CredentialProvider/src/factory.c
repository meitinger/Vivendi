#include "common.h"

#define CLASS ClassFactory

DEFINE(
    ICredentialProvider *pProvider;
    ,
    ,
    CLEANUP_RELEASE(_(pProvider)));

METHOD(CreateInstance, _In_opt_ IUnknown *pUnkOuter, _In_ REFIID riid, _COM_Outptr_ void **ppvObject)
{
    CHECK_AND_INIT_POINTER(ppvObject);
    CHECK_POINTER(riid);
    CHECK(pUnkOuter == NULL, CLASS_E_NOAGGREGATION);

    HRESULT hr = S_OK;

    if (_(pProvider) == NULL)
    {
        CO_CALL(NewCredentialProvider(&_(pProvider)));
    }
    CO_CALL(_(pProvider)->lpVtbl->QueryInterface(_(pProvider), riid, ppvObject));

CO_FINALLY:
    return hr;
}

METHOD(LockServer, BOOL fLock)
{
    if (fLock)
    {
        InterlockedIncrement(&g_lLockServerCount);
    }
    else
    {
        InterlockedDecrement(&g_lLockServerCount);
    }
    return S_OK;
}

VTABLE(CreateInstance, LockServer);
