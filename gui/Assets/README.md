# Sommie artwork

The application icon uses Sommie artwork from Fire Emblem Engage, matching the
FETH editor's use of Sothis. No separate avatar is added to the menu bar.

- Source: [Fire Emblem Wiki — Sommie](https://fireemblemwiki.org/wiki/Sommie)
- Image: [FEE_Sommie.png](https://cdn.fireemblemwiki.org/thumb/5/57/FEE_Sommie.png/800px-FEE_Sommie.png)
- Rights: Nintendo / Intelligent Systems. This game artwork is not covered by
  the project's MIT license; no ownership of it is claimed.

`app-icon.png` and `app-icon.ico` preserve the artwork's proportions and use
transparent padding. There are no added letters, borders or backgrounds.
Regenerate them with Pillow installed:

```sh
python tools/make_app_icons.py /path/to/FEE_Sommie.png
```

The PNG is embedded for the macOS Dock. Windows embeds the ICO in
the executable. Release packaging converts the same PNG to the macOS ICNS file.
