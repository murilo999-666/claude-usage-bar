@echo off
rem Builds bin\ClaudeCode.exe with the C# compiler that comes with Windows (.NET Framework 4.x).
cd /d "%~dp0"
if not exist bin mkdir bin
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /codepage:65001 /target:winexe /platform:x64 /optimize+ /win32icon:claude.ico /r:System.Management.dll /out:bin\ClaudeCode.exe ClaudeCode.cs Sessoes.cs Barra.cs Rodape.cs Clawd.cs W32.cs Atalhos.cs
