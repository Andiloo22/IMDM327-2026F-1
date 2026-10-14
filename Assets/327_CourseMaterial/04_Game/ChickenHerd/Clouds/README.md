Cloud noise is adapted from [sebh/TileableVolumeNoise](https://github.com/sebh/TileableVolumeNoise), MIT, copyright 2017 Sébastien Hillaire.

`TileableVolumeNoise.compute` ports the original hash, value noise, Worley cell search, and cloud mixing to HLSL. Perlin uses periodic 3D gradients in place of the original GLM 4D implementation. Frequencies are reduced for the 48³ volume.

Choose **IMDM 327 → Generate cloud noise** to rebuild `CloudNoise.asset`. `FarmCloud.shader` samples this volume to draw drifting clouds. No C++ plugin is needed.
