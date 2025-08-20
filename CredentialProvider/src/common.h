#define STRICT
#include <Windows.h>
#include <ShlGuid.h>
#include <Shlwapi.h>
#include <credentialprovider.h>
#include <winhttp.h>
#include <strsafe.h>

#include "private.h"

extern LONG g_lComObjectsCount;
extern LONG g_lLockServerCount;
extern const CLSID g_clsidProvider;

extern HRESULT NewClassFactory(IClassFactory **ppcf);
extern HRESULT NewCredentialProvider(ICredentialProvider **ppcp);
extern HRESULT NewCredentialProviderCredential(ICredentialProviderCredential **ppcpc);

typedef struct tagVIVENDI_CREDENTIAL_PROVIDER_FIELD
{
    CREDENTIAL_PROVIDER_FIELD_TYPE cpft;
    CREDENTIAL_PROVIDER_FIELD_STATE cpfs;
    CREDENTIAL_PROVIDER_FIELD_INTERACTIVE_STATE cpfis;
    LPCWSTR pszLabel;
    LPCGUID lpguidFieldType;
} VIVENDI_CREDENTIAL_PROVIDER_FIELD;

static const VIVENDI_CREDENTIAL_PROVIDER_FIELD g_vcpf[] = {
    {CPFT_TILE_IMAGE, CPFS_DISPLAY_IN_SELECTED_TILE, CPFIS_NONE, LABEL_TILE_IMAGE, NULL},
    {CPFT_LARGE_TEXT, CPFS_DISPLAY_IN_BOTH, CPFIS_NONE, LABEL_PROVIDER_TEXT, NULL},
    {CPFT_EDIT_TEXT, CPFS_DISPLAY_IN_SELECTED_TILE, CPFIS_FOCUSED, LABEL_USERNAME_TEXT, &CPFG_LOGON_USERNAME},
    {CPFT_PASSWORD_TEXT, CPFS_DISPLAY_IN_SELECTED_TILE, CPFIS_NONE, LABEL_PASSWORD_TEXT, &CPFG_LOGON_PASSWORD},
    {CPFT_SUBMIT_BUTTON, CPFS_DISPLAY_IN_SELECTED_TILE, CPFIS_NONE, LABEL_SUBMIT_BUTTON, NULL},
};

#define CLEANUP(var, op) \
    do                   \
    {                    \
        if (var != NULL) \
        {                \
            op(var);     \
            var = NULL;  \
        }                \
    } while (0)

#define CLEANUP_CO_MEM(var) CLEANUP(var, CoTaskMemFree)

#define CLEANUP_REG_KEY(var) CLEANUP(var, RegCloseKey)

#define CLEANUP_RELEASE(var) CLEANUP(var, (var)->lpVtbl->Release)

#define CLEANUP_ZERO_MEM(var) SecureZeroMemory(&(var), sizeof(var));

#define CO_CALL(call)        \
    do                       \
    {                        \
        hr = (call);         \
        if (FAILED(hr))      \
        {                    \
            goto CO_FINALLY; \
        }                    \
    } while (0)

#define CO_CALLOC(var, size)          \
    do                                \
    {                                 \
        var = CoTaskMemAlloc((size)); \
        if (var == NULL)              \
        {                             \
            hr = E_OUTOFMEMORY;       \
            goto CO_FINALLY;          \
        }                             \
        ZeroMemory(var, (size));      \
    } while (0)

#define CO_WIN32(call)                               \
    do                                               \
    {                                                \
        if (!(call))                                 \
        {                                            \
            hr = HRESULT_FROM_WIN32(GetLastError()); \
            goto CO_FINALLY;                         \
        }                                            \
    } while (0)

#define CO_REG(call)                         \
    do                                       \
    {                                        \
        LSTATUS status = (call);             \
        if (status != ERROR_SUCCESS)         \
        {                                    \
            hr = HRESULT_FROM_WIN32(status); \
            goto CO_FINALLY;                 \
        }                                    \
    } while (0)

#define CHECK(condition, hr) \
    do                       \
    {                        \
        if (!(condition))    \
        {                    \
            return hr;       \
        }                    \
    } while (0)

#define CHECK_POINTER(ptr) CHECK((ptr) != NULL, E_POINTER)

#define CHECK_FIELD_IN_RANGE(index) CHECK((index) < ARRAYSIZE(g_vcpf), E_INVALIDARG)

#define CHECK_AND_INIT_POINTER(ptr) \
    do                              \
    {                               \
        if ((ptr) == NULL)          \
        {                           \
            return E_POINTER;       \
        }                           \
        *(ptr) = NULL;              \
    } while (0)

#define CONCAT_INNER(a, b) a##b
#define CONCAT(x, y) CONCAT_INNER(x, y)
#define IFACE CONCAT(I, CLASS)
#define VTBL_TYPE CONCAT(IFACE, Vtbl)
#define VTBL_VAR CONCAT(g_vtbl, CLASS)

#define _(member) (((CLASS *)This)->member)

#define DEFINE(definition, initialize, finalize)                            \
    typedef struct CONCAT(tag, CLASS)                                       \
    {                                                                       \
        VTBL_TYPE *lpVtbl;                                                  \
        LONG lRefCount;                                                     \
        definition                                                          \
    } CLASS;                                                                \
    static VTBL_TYPE VTBL_VAR;                                              \
    HRESULT CONCAT(New, CLASS)(IFACE * *pp)                                 \
    {                                                                       \
        CHECK_AND_INIT_POINTER(pp);                                         \
        HRESULT hr = S_OK;                                                  \
        IFACE *This = NULL;                                                 \
        CO_CALLOC(This, sizeof(CLASS));                                     \
        _(lpVtbl) = &VTBL_VAR;                                              \
        _(lRefCount) = 1;                                                   \
        initialize;                                                         \
    CO_FINALLY:                                                             \
        if (FAILED(hr))                                                     \
        {                                                                   \
            CLEANUP_CO_MEM(This);                                           \
        }                                                                   \
        else                                                                \
        {                                                                   \
            InterlockedIncrement(&g_lComObjectsCount);                      \
            *pp = This;                                                     \
        }                                                                   \
        return hr;                                                          \
    }                                                                       \
    METHOD(QueryInterface, _In_ REFIID riid, _COM_Outptr_ void **ppvObject) \
    {                                                                       \
        CHECK_AND_INIT_POINTER(ppvObject);                                  \
        CHECK_POINTER(riid);                                                \
        CHECK(                                                              \
            (                                                               \
                IsEqualIID(riid, &IID_IUnknown) ||                          \
                IsEqualIID(riid, &CONCAT(IID_, IFACE))),                    \
            E_NOINTERFACE);                                                 \
        *ppvObject = This;                                                  \
        This->lpVtbl->AddRef(This);                                         \
        return S_OK;                                                        \
    }                                                                       \
    static ULONG STDMETHODCALLTYPE AddRef(IFACE *This)                      \
    {                                                                       \
        return InterlockedIncrement(&_(lRefCount));                         \
    }                                                                       \
    static ULONG STDMETHODCALLTYPE Release(IFACE *This)                     \
    {                                                                       \
        LONG lRefCount = InterlockedDecrement(&_(lRefCount));               \
        if (lRefCount == 0)                                                 \
        {                                                                   \
            InterlockedDecrement(&g_lComObjectsCount);                      \
            finalize;                                                       \
            CLEANUP_CO_MEM(This);                                           \
        }                                                                   \
        return lRefCount;                                                   \
    }

#define METHOD(name, ...) static HRESULT STDMETHODCALLTYPE name(IFACE *This __VA_OPT__(, ) __VA_ARGS__)

#define VTABLE(...) static VTBL_TYPE VTBL_VAR = {QueryInterface, AddRef, Release __VA_OPT__(, ) __VA_ARGS__};
