@if not defined _echo @echo off

REM test.cmd will bootstrap the cli and ultimately call "dotnet build".
REM If no configuration is specified, the default configuration will be
REM set to netcore_Debug (see config.json).

@call run.cmd test "'-Project=..\test\Mono.Linker.Tests\Mono.Linker.Tests.csproj'" %*
@exit /b %ERRORLEVEL%
