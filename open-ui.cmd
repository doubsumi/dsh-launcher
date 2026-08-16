@echo off
rem Open the DeepSeek Harness web UI in the default browser.
set "PORT=3080"
if defined DSH_PORT set "PORT=%DSH_PORT%"
start "" "http://127.0.0.1:%PORT%"
