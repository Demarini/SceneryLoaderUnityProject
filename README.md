# Scenery Loader — Unity Project Template

A starter Unity project for building **scenery asset bundles** for the game **Puck** (by NS7). Clone this, drop in your custom scenery, and use the bundled editor tools to build, encrypt, and export a pack ready to be loaded into the game.

## Requirements

- **Unity 6000.2.15f1** (Unity 6) — open with Unity Hub. Other Unity 6 versions *may* work, but were not tested.
- **Git** and **[Git LFS](https://git-lfs.com/)** — required to clone the project correctly. Without LFS, the binary asset files (textures, audio, fonts) will be downloaded as small pointer placeholders instead of real files.
- **Windows** target — bundles are built for `StandaloneWindows64`.

## Getting started

### 1. Install Git LFS (one time per machine)

```sh
git lfs install
```

### 2. Clone

```sh
git clone https://github.com/Demarini/SceneryLoaderUnityProject.git
```

LFS files (PNGs, audio, fonts) will fetch automatically as part of the clone. If you already cloned before installing LFS, run `git lfs pull` from inside the project folder.

### 3. Open in Unity Hub

Add the cloned folder as a project in Unity Hub and open it with **Unity 6000.2.15f1**. The first import will take several minutes — Unity is rebuilding the `Library/` cache, which isn't committed.

### 4. Open the default scene

`Assets/Scenes/DefaultScene.unity` is the starter scene. Drop your scenery prefab in, set it up to your liking, then build.

## Building a bundle

Editor scripts add a **Tools → Bundles** menu in Unity:

- **Build, Encrypt + Write Info…** — opens a small window with toggles (e.g. *Use Glass*), then builds **all** bundles, encrypts them, and writes `AssetInformation.json` next to the output.
- **Build Selected Bundle…** — full UI with lighting, audio, shadow, and reflection tuning for a single bundle.
- **Build & Encrypt ALL Bundles (no UI)** — headless build with defaults.

Output goes to `Build/AssetBundles/` (gitignored). Each bundle ships as both the raw `.bundle` and an encrypted `.abx`, alongside `AssetInformation.json`.

## What's in this repo

| Path | Purpose |
| --- | --- |
| `Assets/Editor/` | Bundle build, encryption, and authoring tools. |
| `Assets/PuckAssets-main/` | Reference assets from the game (rink prefab, audio, fonts, country flags, materials). See attribution below. |
| `Assets/Scenes/DefaultScene.unity` | Starter scene to build your scenery in. |
| `ProjectSettings/` | URP, Input System, layers, quality settings — required for the project to open. |
| `Packages/` | Package manifest (URP, Input System, etc.). |

Personal work-in-progress assets, the original creator's scenery packs, and per-machine files (`Library/`, `Temp/`, `obj/`, IDE files) are intentionally excluded.

## Attribution & licensing

- **Editor scripts** (`Assets/Editor/`) and project configuration are released under the [MIT License](LICENSE).
- **`Assets/PuckAssets-main/`** contains assets from the game **Puck**, property of **NS7**, included with permission. Those assets are **not** covered by the MIT license and are governed by NS7's own terms — use them only in the context of building content for Puck.

## Contributing

Issues and PRs welcome. If you build something cool with this template, drop a link.
