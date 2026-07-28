# ZiaConvert

Convertisseur video et audio, avec interface graphique et ligne de commande. ZiaConvert n'implemente aucun algorithme de conversion lui-meme : c'est un orchestrateur au-dessus de [FFmpeg](https://ffmpeg.org/), qui gere le routage, la progression, l'annulation propre et le parallelisme.

## Telecharger

La derniere version compilee (Windows 64 bits, rien a installer) est disponible dans les [releases](https://github.com/Aymezia/ZiaConvert/releases). Dezipper et lancer `ZiaConvert.exe`.

## Fonctionnalites

- **Copie de flux sans reencodage** quand le conteneur cible accepte les codecs source (`mp4` vers `mkv` en quelques secondes plutot qu'en plusieurs minutes), avec detection automatique et repli sur le reencodage sinon.
- **Acceleration materielle** (NVENC, QuickSync, AMF) detectee par un veritable encodage de test au demarrage, pas par simple lecture de la liste des encodeurs compiles dans FFmpeg — cette liste annonce souvent des encodeurs que le materiel ne sait pas executer.
- **Changement de cadence** en deux modes distincts : duplication d'images (instantane, mais n'ajoute aucune fluidite reelle) ou interpolation par analyse de mouvement (`minterpolate`, plus lent mais produit de vraies images intermediaires).
- **GIF anime** avec palette de couleurs calculee sur le contenu, en une seule passe.
- **Decoupe** par debut/fin, extraction audio, normalisation de volume (EBU R128).
- **File de conversions** avec parallelisme borne par famille de format (une video sature deja tous les coeurs, inutile d'en lancer dix en parallele), annulation individuelle ou globale, sans jamais laisser de fichier de sortie partiel.
- **Prereglages** (Rapide, Web, Qualite, Archivage) qui remplissent les champs plutot que de les masquer derriere un mode « personnalise ».

## Formats

| Entree | Sortie |
|---|---|
| mp4, mkv, webm, mov, avi, m4v, wmv, flv, mpg, ts, ogv, 3gp | mp4, mkv, webm, mov, avi, m4v, wmv, flv, mpg, ts, ogv, 3gp, gif |
| — | mp3, aac, m4a, flac, wav, opus, ogg, wma |

L'extraction audio fonctionne depuis n'importe quel fichier video ou audio pris en charge.

## Compiler depuis les sources

Necessite le [SDK .NET 10](https://dotnet.microsoft.com/download) et [FFmpeg](https://www.gyan.dev/ffmpeg/builds/) (build « full ») accessible dans le `PATH` ou place dans `tools/ffmpeg/` a la racine du projet.

```bash
dotnet build ZiaConvert.slnx
dotnet test ZiaConvert.slnx
dotnet run --project src/ZiaConvert.App
```

### Publier un executable autonome

```bash
dotnet publish src/ZiaConvert.App -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
dotnet publish src/ZiaConvert.Cli -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
```

Copier `ffmpeg.exe` et `ffprobe.exe` dans `dist/tools/ffmpeg/` pour obtenir un dossier autonome.

## Ligne de commande

```bash
zia film.mp4 -o film.mkv                                   # remux, aucune perte
zia film.mkv -o film.mp4 --codec h264 -q 20                 # reencodage
zia clip.mp4 -o clip.gif --fps 15 -w 480 -ss 5 -to 10        # extrait en GIF
zia concert.mp4 -o concert.mp3 -b 192k                       # extraction audio
zia video.mp4 -o video-60fps.mp4 --fps 60 --interpolate      # vraie interpolation
zia engines                                                   # moteurs et materiel detecte
zia --help                                                    # toutes les options
```

## Architecture

```
src/
├── ZiaConvert.Core/       modeles, routage, file d'attente — aucune dependance a un moteur
├── ZiaConvert.Engines/    moteur FFmpeg (arguments, progression, detection materielle)
├── ZiaConvert.App/        interface Avalonia
└── ZiaConvert.Cli/        ligne de commande
tests/
├── ZiaConvert.Core.Tests/
└── ZiaConvert.Engines.Tests/   dont des tests d'integration qui pilotent reellement ffmpeg
```

Tout moteur de conversion implemente une seule interface, `IConversionEngine` :

```csharp
public interface IConversionEngine
{
    string Name { get; }
    EngineAvailability CheckAvailability();
    bool CanHandle(ConversionRequest request);
    IAsyncEnumerable<ConversionProgress> ExecuteAsync(
        ConversionRequest request, CancellationToken cancellationToken = default);
}
```

Le reste de l'application (file d'attente, interface, ligne de commande) ne connait que cette interface : ajouter un moteur ne demande de modification nulle part ailleurs.

## Licence

[GPL v3](LICENSE.txt). ZiaConvert est livre avec un binaire FFmpeg compile en GPL v3 (`libx264`/`libx265` inclus), ce qui impose cette licence a l'ensemble — voir [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) pour le detail des composants tiers et les alternatives possibles.
