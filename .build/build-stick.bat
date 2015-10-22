@rem Runs build then stays open

@echo off
start cmd.exe /k "python ./build.py"
title "Finished build"
