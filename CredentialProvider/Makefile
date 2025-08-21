SRC_DIR = src
OBJ_DIR = obj
BIN_DIR = bin

DLL_NAME = VivendiCredentialProvider.dll
DLL_PATH = $(BIN_DIR)\$(DLL_NAME)

all: $(DLL_PATH)

clean:
	-RMDIR /S /Q $(OBJ_DIR)
	-RMDIR /S /Q $(BIN_DIR)

$(OBJ_DIR)\*.obj: Makefile $(SRC_DIR)\common.h $(SRC_DIR)\private.h $(SRC_DIR)\resources.h

$(OBJ_DIR)\*.res: Makefile $(SRC_DIR)\resources.h $(SRC_DIR)\tile.bmp

{$(SRC_DIR)}.rc{$(OBJ_DIR)}.res:
	-MKDIR $(OBJ_DIR)
	RC.EXE /fo$@ $<

{$(SRC_DIR)}.c{$(OBJ_DIR)}.obj:
	-MKDIR $(OBJ_DIR)
	CL.EXE /c /Zc:preprocessor /Tc$< /Fo$@

$(DLL_PATH): $(SRC_DIR)\export.def $(OBJ_DIR)\credential.obj $(OBJ_DIR)\dllmain.obj $(OBJ_DIR)\factory.obj $(OBJ_DIR)\provider.obj $(OBJ_DIR)\resources.res
	-MKDIR $(BIN_DIR)
	LINK.EXE /DLL /ENTRY:DllMain /OUT:$@ /DEF:$** libvcruntime.lib Advapi32.lib Kernel32.lib Netapi32.lib Shlwapi.lib Ole32.lib User32.lib Winhttp.lib
