@echo off
rem Lance fond-parc.ps1 sans modifier la strategie d'execution des scripts de Windows (autorisation pour cette commande seulement).
rem Usage : deploy\fond-parc.cmd debian@IP_DU_VPS
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0fond-parc.ps1" %*
