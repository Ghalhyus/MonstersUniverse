@echo off
rem Lance publier.ps1 sans modifier la strategie d'execution des scripts de Windows (autorisation pour cette commande seulement).
rem Usage : deploy\publier.cmd debian@IP_DU_VPS
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publier.ps1" %*
