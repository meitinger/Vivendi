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

    if (g_vcpf[dwFieldID].cpft == CPFT_TILE_IMAGE)
    {
        *phbmp = LoadBitmapW(g_hinstDLL, MAKEINTRESOURCEW(IDB_TILE_IMAGE));
        return *phbmp == NULL ? HRESULT_FROM_WIN32(GetLastError()) : S_OK;
    }
    else
    {
        return E_INVALIDARG;
    }
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
    USER_INFO_1 *puiExistingUser = NULL;
    USER_INFO_1 uiNewUser = {0};
    NET_API_STATUS naStatus = NERR_Success;
    const DWORD dwRequiredFlags = UF_PASSWD_CANT_CHANGE | UF_DONT_EXPIRE_PASSWD;
    const DWORD dwForbiddenFlags = UF_ACCOUNTDISABLE | UF_PASSWD_NOTREQD | UF_LOCKOUT | UF_PASSWORD_EXPIRED;

    CO_WIN32(hSession = WinHttpOpen(PROVIDER_NAME, WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY, WINHTTP_NO_PROXY_NAME, WINHTTP_NO_PROXY_BYPASS, WINHTTP_FLAG_SECURE_DEFAULTS));
    CO_WIN32(hConnect = WinHttpConnect(hSession, SERVER_NAME, SERVER_PORT, 0));
    CO_WIN32(hRequest = WinHttpOpenRequest(hConnect, NULL, OBJECT_NAME, NULL, WINHTTP_NO_REFERER, WINHTTP_DEFAULT_ACCEPT_TYPES, WINHTTP_FLAG_BYPASS_PROXY_CACHE | WINHTTP_FLAG_SECURE));
    CO_WIN32(WinHttpSetCredentials(hRequest, WINHTTP_AUTH_TARGET_SERVER, WINHTTP_AUTH_SCHEME_DIGEST, _(szUserName), _(szPassword), NULL));
    CO_WIN32(WinHttpSendRequest(hRequest, WINHTTP_NO_ADDITIONAL_HEADERS, 0, WINHTTP_NO_REQUEST_DATA, 0, 0, 0));
    CO_WIN32(WinHttpReceiveResponse(hRequest, NULL));
    CO_WIN32(WinHttpQueryHeaders(hRequest, WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER, WINHTTP_HEADER_NAME_BY_INDEX, &dwStatusCode, &dwStatusCodeSize, WINHTTP_NO_HEADER_INDEX));

    if (dwStatusCodeSize == HTTP_STATUS_NO_CONTENT)
    {
        naStatus = NetUseGetInfo(NULL, _(szUserName), 1, &puiExistingUser);
        if (naStatus == NERR_UserNotFound)
        {
            uiNewUser.usri1_name = _(szUserName);
            uiNewUser.usri1_password = _(szPassword);
            uiNewUser.usri1_password_age = 0;
            uiNewUser.usri1_priv =USER_PRIV_USER;
            uiNewUser.usri1_home_dir = NULL;
            uiNewUser.usri1_comment = NULL;
            uiNewUser.usri1_flags = dwRequiredFlags;
            CO_NET(NetUserAdd(NULL, 1, &uiNewUser, NULL));
        }
        else
        {
            CO_NET(naStatus);
            DWORD dwNewFlags = (puiExistingUser->usri1_flags | dwRequiredFlags) & ~dwForbiddenFlags;
            if (puiExistingUser->usri1_flags != dwNewFlags) {
                puiExistingUser->usri1_flags = dwNewFlags;
                CO_NET(NetUserSetInfo(NULL, _(szUserName), 1, puiExistingUser, NULL));
            }
            CO_NET(NetUserChangePassword(NULL, _(szUserName), NULL, _(szPassword)));
        }
    }
    else
    {

    }
    //  CPGSR_NO_CREDENTIAL_NOT_FINISHED
    // CPGSR_RETURN_CREDENTIAL_FINISHED
    // HTTP_STATUS_NO_CONTENT

CO_FINALLY:
    CLEANUP(hRequest, WinHttpCloseHandle);
    CLEANUP(hConnect, WinHttpCloseHandle);
    CLEANUP(hSession, WinHttpCloseHandle);
    CLEANUP(puiExistingUser, NetApiBufferFree);
    if (FAILED(hr))
    {
        LPWSTR pszMessage = NULL;
        *pcpgsr = CPGSR_NO_CREDENTIAL_NOT_FINISHED;
        if (FormatMessageW(FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM, NULL, hr, 0, &pszMessage, 0, NULL))
        {
            hr = SHStrDupW(pszMessage, ppszOptionalStatusText);
        }
        else
        {
            hr = SHStrDupW(L"Fehler bei der Anmeldung.", ppszOptionalStatusText);
        }
        *pcpsiOptionalStatusIcon = CPSI_ERROR;
        CLEANUP(pszMessage, LocalFree);
    }
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
