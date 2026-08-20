# Diva Assistant

Application Windows unique pour les cartouches documentaires ARSEF, les organigrammes et Diva Productivité.

## Fonctions

- génère les documents Word depuis les modèles intégrés, applique leurs styles et ouvre le `.docx` créé ;
- reprend une session après redémarrage et produit le PDF uniquement avec **Document fini** ;
- ajoute, après confirmation, le document au registre Excel choisi avec sauvegarde et détection de conflit ;
- conserve les organigrammes et missions par compte ;
- synchronise une copie AES-256-GCM chiffrée de chaque profil dans le OneDrive partagé ;
- ne collecte aucune télémétrie ; les erreurs restent dans le profil Windows local ;
- vérifie les mises à jour GitHub par manifeste ECDSA signé, sauvegarde l’installation et revient à la version précédente si le nouveau lancement échoue.

Les données locales et réglages sont sous `%APPDATA%\Diva Assistant`. Les documents restent dans `ARSEF` sur le vrai Bureau Windows. Le contenu partagé est sous `Diva Productivite` dans OneDrive.

## Construire

Prérequis : SDK .NET 8 et Inno Setup 6.

```powershell
dotnet build .\DivaAssistant.csproj -c Release -warnaserror
.\build-release.ps1
```

La clé privée de mise à jour n’est jamais placée dans le dépôt. Le script utilise `DIVA_UPDATE_SIGNING_KEY`, ou `%APPDATA%\Diva Assistant Release Signing\update-signing-private-key.pem` localement.
