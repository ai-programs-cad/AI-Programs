# Third-party components

- SOLIDWORKS and its API/interop assemblies are proprietary prerequisites. They are not included. PartCraft builds against API assemblies from the developer's licensed installation.
- Open CASCADE Technology is used for STEP handling. It is separately licensed under LGPL 2.1 with the OCCT additional exception. Build it separately; preserve its upstream notices and corresponding-source requirements when shipping it. The original packaging used OCCT 7.9.3. The existing CAD build scripts accept an external OCCT source/build directory.
- Python is separately licensed under the PSF license and associated bundled-component notices. A Python runtime is not part of this source repository; retain its full notices if packaging it.
- Windows/.NET and compiler toolchains are external prerequisites. Original toolchain notices retained under apps/cad-preview/third_party/licenses are not the license for AI Programs code.

Do not apply the repository's GPL notice to third-party components that carry their own license.
