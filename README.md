# AI Programs

Free, community-improvable tools for everyday CAD and manufacturing work.

I built these tools for tasks I repeatedly face in my own work. They help me spend less time on routine operations, and I am sharing them so others can use them and improve them too.

| Project | Purpose | Requirements |
| --- | --- | --- |
| CAD Preview | Explorer thumbnails and interactive previews of DXF, STL and STEP | Windows x64; STEP support uses Open CASCADE |
| CNC File Doctor | Convert splines to smooth polylines within a chosen tolerance; inspect and prepare DXF contours | Windows for GUI; Python for engine/tests |
| PartCraft | Sheet-metal flat patterns with bend lines, DXF from selected parallel faces including slots/pockets, and profile cutting cards/schedules | Licensed SOLIDWORKS 2025 x64 and .NET Framework 4.8 |
| SOLIDWORKS macros | Flat DXF export, part information, profile cutting, Cut List/body naming | SOLIDWORKS and its VBA environment |

No payment, trial timer or activation key is required for the community source edition. SOLIDWORKS itself is not included and is not made free by this project.

**Current status:** prepared for the initial public release. Core functionality was checked on the author's workstation; community builds and installation on other computers need their own validation. Existing files and models should be backed up before testing modifications. Review output before manufacturing.

## Get started

- Read [build instructions](docs/BUILD.md).
- Read the [Russian introduction](README.ru.md).
- Report a reproducible problem with the GitHub issue templates.
- To contribute a fix, read [CONTRIBUTING](CONTRIBUTING.md) and submit a pull request.
- See [known limits](docs/STATUS.md) and [third-party dependencies](THIRD_PARTY_NOTICES.md).

Original project code: GPL-3.0-or-later, with the SOLIDWORKS interoperability permission in LICENSE-NOTICE.md. Third-party components retain their own licenses.

The repository intentionally excludes customer models, private activation keys, local Windows profiles, credentials and proprietary SOLIDWORKS DLLs.
