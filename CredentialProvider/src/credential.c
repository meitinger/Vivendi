#include "common.h"

#define CLASS CredentialProviderCredential

DEFINE(
    ICredentialProviderCredentialEvents *pEvents;
    WCHAR szUserName[MAX_USERNAME_LEN + 1];
    WCHAR szPassword[MAX_PASSWORD_LEN + 1];
    ,
    ,
    CLEANUP_RELEASE(_(pEvents));
    CLEANUP_ZERO_MEM(_(szUserName));
    CLEANUP_ZERO_MEM(_(szPassword));)

METHOD(Advise, _In_ ICredentialProviderCredentialEvents *pcpce)
{
    CHECK_POINTER(pcpce);

    CLEANUP_RELEASE(_(pEvents));
    return pcpce->lpVtbl->QueryInterface(pcpce, &IID_ICredentialProviderCredentialEvents, &_(pEvents));
}

METHOD(UnAdvise)
{
    CLEANUP_RELEASE(_(pEvents));
    return S_OK;
}

METHOD(SetSelected, _Out_ BOOL *pbAutoLogon)
{
    CHECK_POINTER(pbAutoLogon);

    *pbAutoLogon = FALSE;
    return S_OK;
}

METHOD(SetDeselected)
{
    HRESULT hr = S_OK;

    CLEANUP_ZERO_MEM(_(szUserName));
    CLEANUP_ZERO_MEM(_(szPassword));
    if (_(pEvents) != NULL)
    {
        for (DWORD dwFieldID = 0; dwFieldID < ARRAYSIZE(g_vcpf); dwFieldID++)
        {
            switch (g_vcpf[dwFieldID].cpft)
            {
            case CPFT_EDIT_TEXT:
                CO_CALL(_(pEvents)->lpVtbl->SetFieldString(_(pEvents), This, dwFieldID, _(szUserName)));
                break;
            case CPFT_PASSWORD_TEXT:
                CO_CALL(_(pEvents)->lpVtbl->SetFieldString(_(pEvents), This, dwFieldID, _(szPassword)));
                break;
            }
        }
    }

CO_FINALLY:
    return hr;
}

METHOD(GetFieldState, DWORD dwFieldID, _Out_ CREDENTIAL_PROVIDER_FIELD_STATE *pcpfs, _Out_ CREDENTIAL_PROVIDER_FIELD_INTERACTIVE_STATE *pcpfis)
{
    CHECK_POINTER(pcpfs);
    CHECK_POINTER(pcpfis);
    CHECK_FIELD_IN_RANGE(dwFieldID);

    *pcpfs = g_vcpf[dwFieldID].cpfs;
    *pcpfis = g_vcpf[dwFieldID].cpfis;
    return S_OK;
}

METHOD(GetStringValue, DWORD dwFieldID, _Outptr_result_nullonfailure_ LPWSTR *ppsz)
{
    CHECK_AND_INIT_POINTER(ppsz);
    CHECK_FIELD_IN_RANGE(dwFieldID);

    switch (g_vcpf[dwFieldID].cpft)
    {
    case CPFT_EDIT_TEXT:
        return SHStrDupW(_(szUserName), ppsz);
    case CPFT_PASSWORD_TEXT:
        return SHStrDupW(_(szPassword), ppsz);
    default:
        return SHStrDupW(g_vcpf[dwFieldID].pszLabel, ppsz);
    }
}

METHOD(GetBitmapValue, DWORD dwFieldID, _Outptr_result_nullonfailure_ HBITMAP *phbmp)
{
    CHECK_AND_INIT_POINTER(phbmp);
    CHECK_FIELD_IN_RANGE(dwFieldID);

    return g_vcpf[dwFieldID].cpft == CPFT_TILE_IMAGE ? E_NOTIMPL : E_INVALIDARG;
}

METHOD(GetCheckboxValue, DWORD dwFieldID, _Out_ BOOL *pbChecked, _Outptr_result_nullonfailure_ LPWSTR *ppszLabel)
{
    CHECK_AND_INIT_POINTER(ppszLabel);
    CHECK_POINTER(pbChecked);

    return E_INVALIDARG;
}

METHOD(GetSubmitButtonValue, DWORD dwFieldID, _Out_ DWORD *pdwAdjacentTo)
{
    CHECK_POINTER(pdwAdjacentTo);
    CHECK_FIELD_IN_RANGE(dwFieldID);

    if (g_vcpf[dwFieldID].cpft != CPFT_SUBMIT_BUTTON)
    {
        return E_INVALIDARG;
    }
    *pdwAdjacentTo = dwFieldID - 1;
    return S_OK;
}

METHOD(GetComboBoxValueCount, DWORD dwFieldID, _Out_ DWORD *pcItems, _Out_ DWORD *pdwSelectedItem)
{
    CHECK_POINTER(pcItems);
    CHECK_POINTER(pdwSelectedItem);

    return E_INVALIDARG;
}

METHOD(GetComboBoxValueAt, DWORD dwFieldID, DWORD dwItem, _Outptr_result_nullonfailure_ LPWSTR *ppszItem)
{
    CHECK_AND_INIT_POINTER(ppszItem);

    return E_INVALIDARG;
}

METHOD(SetStringValue, DWORD dwFieldID, _In_ LPCWSTR psz)
{
    CHECK_POINTER(psz);
    CHECK_FIELD_IN_RANGE(dwFieldID);

    switch (g_vcpf[dwFieldID].cpft)
    {
    case CPFT_EDIT_TEXT:
        CLEANUP_ZERO_MEM(_(szUserName));
        return StringCbCopyW(_(szUserName), sizeof(_(szUserName)), psz);
    case CPFT_PASSWORD_TEXT:
        CLEANUP_ZERO_MEM(_(szPassword));
        return StringCbCopyW(_(szPassword), sizeof(_(szPassword)), psz);
    default:
        return E_INVALIDARG;
    }
}

METHOD(SetCheckboxValue, DWORD dwFieldID, BOOL bChecked)
{
    return E_INVALIDARG;
}

METHOD(SetComboBoxSelectedValue, DWORD dwFieldID, DWORD dwSelectedItem)
{
    return E_INVALIDARG;
}

METHOD(CommandLinkClicked, DWORD dwFieldID)
{
    return E_INVALIDARG;
}

METHOD(GetSerialization, _Out_ CREDENTIAL_PROVIDER_GET_SERIALIZATION_RESPONSE *pcpgsr, _Out_ CREDENTIAL_PROVIDER_CREDENTIAL_SERIALIZATION *pcpcs, _Outptr_result_maybenull_ LPWSTR *ppszOptionalStatusText, _Out_ CREDENTIAL_PROVIDER_STATUS_ICON *pcpsiOptionalStatusIcon)
{
    CHECK_AND_INIT_POINTER(ppszOptionalStatusText);
    CHECK_POINTER(pcpsiOptionalStatusIcon);
    CHECK_POINTER(pcpgsr);
    CHECK_POINTER(pcpcs);

    HRESULT hr = S_OK;
    HINTERNET hSession;
    HINTERNET hConnect;
    HINTERNET hRequest;
    DWORD dwStatusCode = 0;
    DWORD dwStatusCodeSize = sizeof(DWORD);

    CO_WIN32(hSession = WinHttpOpen(PROVIDER_NAME, WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY, WINHTTP_NO_PROXY_NAME, WINHTTP_NO_PROXY_BYPASS, WINHTTP_FLAG_SECURE_DEFAULTS));
    CO_WIN32(hConnect = WinHttpConnect(hSession, SERVER_NAME, SERVER_PORT, 0));
    CO_WIN32(hRequest = WinHttpOpenRequest(hConnect, NULL, OBJECT_NAME, NULL, WINHTTP_NO_REFERER, WINHTTP_DEFAULT_ACCEPT_TYPES, WINHTTP_FLAG_BYPASS_PROXY_CACHE | WINHTTP_FLAG_SECURE));
    CO_WIN32(WinHttpSetCredentials(hRequest, WINHTTP_AUTH_TARGET_SERVER, WINHTTP_AUTH_SCHEME_DIGEST, _(szUserName), _(szPassword), NULL));
    CO_WIN32(WinHttpSendRequest(hRequest, WINHTTP_NO_ADDITIONAL_HEADERS, 0, WINHTTP_NO_REQUEST_DATA, 0, 0, 0));
    CO_WIN32(WinHttpReceiveResponse(hRequest, NULL));
    CO_WIN32(WinHttpQueryHeaders(hRequest, WINHTTP_QUERY_STATUS_CODE, WINHTTP_HEADER_NAME_BY_INDEX, &dwStatusCode, &dwStatusCodeSize, WINHTTP_NO_HEADER_INDEX));

    // HTTP_STATUS_NO_CONTENT

CO_FINALLY:
    CLEANUP(hRequest, WinHttpCloseHandle);
    CLEANUP(hConnect, WinHttpCloseHandle);
    CLEANUP(hSession, WinHttpCloseHandle);
    return hr;
}

METHOD(ReportResult, NTSTATUS ntsStatus, NTSTATUS ntsSubstatus, _Outptr_result_maybenull_ LPWSTR *ppszOptionalStatusText, _Out_ CREDENTIAL_PROVIDER_STATUS_ICON *pcpsiOptionalStatusIcon)
{
    return E_NOTIMPL;
}

VTABLE(
    Advise,
    UnAdvise,
    SetSelected,
    SetDeselected,
    GetFieldState,
    GetStringValue,
    GetBitmapValue,
    GetCheckboxValue,
    GetSubmitButtonValue,
    GetComboBoxValueCount,
    GetComboBoxValueAt,
    SetStringValue,
    SetCheckboxValue,
    SetComboBoxSelectedValue,
    CommandLinkClicked,
    GetSerialization,
    ReportResult);
