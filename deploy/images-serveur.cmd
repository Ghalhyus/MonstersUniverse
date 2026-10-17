@echo off
rem Lance images-serveur.ps1 sans modifier la strategie d'execution des scripts de Windows (autorisation pour cette commande seulement).
rem Usage : deploy\images-serveur.cmd debian@IP_DU_VPS
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0images-serveur.ps1" %*
