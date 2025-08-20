#include "common.h"

#define CLASS CredentialProvider

DEFINE(
    ICredentialProviderCredential *pCredential;
    ,
    ,
    CLEANUP_RELEASE(_(pCredential)));

METHOD(SetUsageScenario, CREDENTIAL_PROVIDER_USAGE_SCENARIO cpus, DWORD dwFlags)
{
    return cpus == CPUS_LOGON ? S_OK : E_NOTIMPL;
}

METHOD(SetSerialization, _In_ const CREDENTIAL_PROVIDER_CREDENTIAL_SERIALIZATION *pcpcs)
{
    return E_NOTIMPL;
}

METHOD(Advise, _In_ ICredentialProviderEvents *pcpe, _In_ UINT_PTR upAdviseContext)
{
    return E_NOTIMPL;
}

METHOD(UnAdvise)
{
    return E_NOTIMPL;
}

METHOD(GetFieldDescriptorCount, _Out_ DWORD *pdwCount)
{
    CHECK_POINTER(pdwCount);

    *pdwCount = ARRAYSIZE(g_vcpf);
    return S_OK;
}

METHOD(GetFieldDescriptorAt, DWORD dwIndex, _Outptr_result_nullonfailure_ CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR **ppcpfd)
{
    CHECK_AND_INIT_POINTER(ppcpfd);
    CHECK_FIELD_IN_RANGE(dwIndex);

    HRESULT hr = S_OK;
    LPWSTR pszLabel = NULL;
    CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR *pcpfd = NULL;

    CO_CALL(SHStrDupW(g_vcpf[dwIndex].pszLabel, &pszLabel));
    CO_CALLOC(pcpfd, sizeof(CREDENTIAL_PROVIDER_FIELD_DESCRIPTOR));
    pcpfd->dwFieldID = dwIndex;
    pcpfd->cpft = g_vcpf[dwIndex].cpft;
    pcpfd->pszLabel = pszLabel;
    if (g_vcpf[dwIndex].lpguidFieldType != NULL)
    {
        CopyMemory(&pcpfd->guidFieldType, g_vcpf[dwIndex].lpguidFieldType, sizeof(GUID));
    }

CO_FINALLY:
    if (FAILED(hr))
    {
        CLEANUP_CO_MEM(pszLabel);
        CLEANUP_CO_MEM(pcpfd);
    }
    else
    {
        *ppcpfd = pcpfd;
    }
    return hr;
}

METHOD(GetCredentialCount, _Out_ DWORD *pdwCount, _Out_ DWORD *pdwDefault, _Out_ BOOL *pbAutoLogonWithDefault)
{
    CHECK_POINTER(pdwCount);
    CHECK_POINTER(pdwDefault);
    CHECK_POINTER(pbAutoLogonWithDefault);

    *pdwCount = 1;
    *pdwDefault = CREDENTIAL_PROVIDER_NO_DEFAULT;
    *pbAutoLogonWithDefault = FALSE;
    return S_OK;
}

METHOD(GetCredentialAt, DWORD dwIndex, _COM_Outptr_ ICredentialProviderCredential **ppcpc)
{
    CHECK_AND_INIT_POINTER(ppcpc);
    CHECK(dwIndex == 0, E_INVALIDARG);

    HRESULT hr = S_OK;

    if (_(pCredential) == NULL)
    {
        CO_CALL(NewCredentialProviderCredential(&_(pCredential)));
    }
    CO_CALL(_(pCredential)->lpVtbl->QueryInterface(_(pCredential), &IID_ICredentialProviderCredential, ppcpc));

CO_FINALLY:
    return hr;
}

VTABLE(
    SetUsageScenario,
    SetSerialization,
    Advise,
    UnAdvise,
    GetFieldDescriptorCount,
    GetFieldDescriptorAt,
    GetCredentialCount,
    GetCredentialAt);
