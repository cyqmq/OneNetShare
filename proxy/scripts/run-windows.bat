@echo off
chcp 65001 >nul
REM Windows 原型: 本地正向代理模式
REM 浏览器需设置 HTTP 代理 127.0.0.1:18080, HTTPS 代理 127.0.0.1:18443

set CONFIG=%~dp0config.json
set EXE=%~dp0hotspot-proxy.exe

if not exist "%EXE%" (
    echo 找不到 %EXE%，请先编译
    exit /b 1
)

echo 启动 hotspot-proxy (forward 模式)...
echo 请在浏览器中配置代理: HTTP 127.0.0.1:18080 / HTTPS 127.0.0.1:18443
"%EXE%" --config "%CONFIG%"
