#include "common.h"

LONG g_lComObjectsCount;
LONG g_lLockServerCount;
const CLSID g_clsidProvider = PROVIDER_CLSID;

BOOL WINAPI DllMain(HINSTANCE hinstDLL, DWORD fdwReason, _In_opt_ LPVOID lpvReserved)
{
    UNREFERENCED_PARAMETER(lpvReserved);
    if (fdwReason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(hinstDLL);
    }
    return TRUE;
}

STDAPI DllCanUnloadNow(void)
{
    return (g_lComObjectsCount <= 0 && g_lLockServerCount <= 0) ? S_OK : S_FALSE;
}

STDAPI DllGetClassObject(_In_ REFCLSID rclsid, _In_ REFIID riid, _Out_ LPVOID *ppv)
{
    CHECK_AND_INIT_POINTER(ppv);
    CHECK_POINTER(rclsid);
    CHECK_POINTER(riid);
    CHECK(IsEqualCLSID(rclsid, &g_clsidProvider), CLASS_E_CLASSNOTAVAILABLE);

    HRESULT hr = S_OK;
    IClassFactory *pFactory = NULL;

    CO_CALL(NewClassFactory(&pFactory));
    CO_CALL(pFactory->lpVtbl->QueryInterface(pFactory, riid, ppv));

CO_FINALLY:
    CLEANUP_RELEASE(pFactory);
    return hr;
}

STDAPI DllRegisterServer(void)
{
    HRESULT hr = S_OK;
    LPWSTR pszDllPath = NULL;
    DWORD dwDllPathSize = 0;
    DWORD dwDllPathLen = 0;
    LPOLESTR pszThisClsid = NULL;
    HKEY hkClsid = NULL;
    HKEY hkClsidThis = NULL;
    HKEY hkClsidThisInprocServer = NULL;
    HKEY hkCredentialProviders = NULL;
    HKEY hkCredentialProvidersThis = NULL;

    do
    {
        dwDllPathSize += 200;
        CLEANUP_CO_MEM(pszDllPath);
        CO_CALLOC(pszDllPath, sizeof(WCHAR) * dwDllPathSize);
        CO_WIN32(dwDllPathLen = GetModuleFileNameW(NULL, pszDllPath, dwDllPathSize));
    } while (dwDllPathSize <= dwDllPathLen);
    CO_CALL(StringFromCLSID(&g_clsidProvider, &pszThisClsid));
    CO_REG(RegCreateKeyExW(HKEY_LOCAL_MACHINE, L"SOFTWARE\\Classes\\CLSID", 0, NULL, 0, KEY_CREATE_SUB_KEY, NULL, &hkClsid, NULL));
    CO_REG(RegCreateKeyExW(hkClsid, pszThisClsid, 0, NULL, 0, KEY_CREATE_SUB_KEY, NULL, &hkClsidThis, NULL));
    CO_REG(RegCreateKeyExW(hkClsidThis, L"InprocServer32", 0, NULL, 0, KEY_SET_VALUE, NULL, &hkClsidThisInprocServer, NULL));
    CO_REG(RegSetValueExW(hkClsidThisInprocServer, L"", 0, REG_SZ, (LPCBYTE)pszDllPath, sizeof(WCHAR) * (dwDllPathLen + 1)));
    CO_REG(RegSetValueExW(hkClsidThisInprocServer, L"ThreadingModel", 0, REG_SZ, (LPCBYTE)L"Apartment", sizeof(L"Apartment")));
    CO_REG(RegCreateKeyExW(HKEY_LOCAL_MACHINE, L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Authentication\\Credential Providers", 0, NULL, 0, KEY_CREATE_SUB_KEY, NULL, &hkCredentialProviders, NULL));
    CO_REG(RegCreateKeyExW(hkCredentialProviders, pszThisClsid, 0, NULL, 0, KEY_SET_VALUE, NULL, &hkCredentialProvidersThis, NULL));
    CO_REG(RegSetValueExW(hkCredentialProvidersThis, L"", 0, REG_SZ, (LPCBYTE)PROVIDER_NAME, sizeof(PROVIDER_NAME)));

CO_FINALLY:
    CLEANUP_CO_MEM(pszDllPath);
    CLEANUP_CO_MEM(pszThisClsid);
    CLEANUP_REG_KEY(hkClsid);
    CLEANUP_REG_KEY(hkClsidThis);
    CLEANUP_REG_KEY(hkClsidThisInprocServer);
    CLEANUP_REG_KEY(hkCredentialProviders);
    CLEANUP_REG_KEY(hkCredentialProvidersThis);
    return hr;
}

STDAPI DllUnregisterServer(void)
{
    HRESULT hr = S_OK;
    LPOLESTR pszThisClsid = NULL;
    HKEY hkCredentialProviders = NULL;
    HKEY hkClsid = NULL;

    CO_CALL(StringFromCLSID(&g_clsidProvider, &pszThisClsid));
    CO_REG(RegCreateKeyExW(HKEY_LOCAL_MACHINE, L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Authentication\\Credential Providers", 0, NULL, 0, DELETE | KEY_ENUMERATE_SUB_KEYS | KEY_QUERY_VALUE, NULL, &hkCredentialProviders, NULL));
    CO_REG(RegDeleteTreeW(hkCredentialProviders, pszThisClsid));
    CO_REG(RegCreateKeyExW(HKEY_LOCAL_MACHINE, L"SOFTWARE\\Classes\\CLSID", 0, NULL, 0, DELETE | KEY_ENUMERATE_SUB_KEYS | KEY_QUERY_VALUE, NULL, &hkClsid, NULL));
    CO_REG(RegDeleteTreeW(hkClsid, pszThisClsid));

CO_FINALLY:
    CLEANUP_CO_MEM(pszThisClsid);
    CLEANUP_REG_KEY(hkCredentialProviders);
    CLEANUP_REG_KEY(hkClsid);
    return hr;
}
