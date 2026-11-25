@echo off
chcp 65001 >nul
REM Windows 原型: forward 或 Wintun 全局 TUN 模式

set CONFIG=%~dp0config.json
set EXE=%~dp0hotspot-proxy.exe

if not exist "%EXE%" (
    echo 找不到 %EXE%，请先编译
    exit /b 1
)

if not exist "%~dp0wintun.dll" (
    echo 警告: 未找到 wintun.dll；开启 tun.enabled 前请放置官方 x86 wintun.dll
)

echo 启动 hotspot-proxy...
echo forward 模式请在浏览器中配置代理: HTTP 127.0.0.1:18080 / HTTPS 127.0.0.1:18443
echo tun.enabled=true 时需要管理员权限，并会临时接管 IPv4 默认路由与 DNS
"%EXE%" --config "%CONFIG%"
