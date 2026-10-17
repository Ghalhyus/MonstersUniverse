@echo off
rem Recupere les images du site (Pokemon, objets, sprites Donjon Mystere) depuis le serveur, ou elles sont stockees
rem (~/pokebank-images), dans PokeBank\wwwroot\images. A lancer apres avoir clone le depot, et quand elles changent.
rem Il faut un acces SSH au serveur. Une seule archive transite (bien plus rapide que des milliers de petits fichiers).
rem Usage : outils\recuperer-images.cmd debian@IP_DU_VPS
rem (Sans acces au serveur : python outils\images.py les reconstruit depuis leurs projets d'origine.)
if "%~1"=="" (
    echo Usage : outils\recuperer-images.cmd debian@IP_DU_VPS
    exit /b 1
)
set "IMAGES=%~dp0..\PokeBank\wwwroot\images"
if not exist "%IMAGES%" mkdir "%IMAGES%"
echo == Recuperation des images depuis %~1
rem Pipe de cmd (pas de PowerShell 5, qui abime les donnees binaires) ; tar de Windows pour decompresser.
ssh %1 "tar -czf - -C pokebank-images ." | tar -xzf - -C "%IMAGES%"
if errorlevel 1 (
    echo Echec de la recuperation (adresse, acces SSH, ou images absentes du serveur : deploy\images-serveur.cmd).
    exit /b 1
)
echo Images recuperees dans PokeBank\wwwroot\images.
