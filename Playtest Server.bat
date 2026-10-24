@echo off
rem SWRP playtest server: runs this project folder (not the sbox.game package) on s&box's dedicated server,
rem so friends join it from the s&box server list without needing access to the package.
rem Close the s&box editor first: both use the same saves folder and the same local port.
rem You join it too: start s&box and type "connect local" in the console (or find "SWRP Playtest" in the server list).
rem Close this window to stop the server.
title SWRP Playtest Server
cd /d "X:\SteamLibrary\steamapps\common\sbox"
sbox-server.exe +game "%~dp0swrp.sbproj" +hostname "SWRP Playtest" +net_allow_local 1
pause
