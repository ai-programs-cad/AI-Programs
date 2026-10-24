# Contributing

1. Open an issue describing the task or defect before a large change.
2. Fork the repository and create a small focused branch.
3. Add a meaningful regression test for a geometry or safety change.
4. Run the relevant tests in docs/BUILD.md and attach the results.
5. Submit a pull request explaining the before/after behavior and limitations.

Never upload customer models, personal paths, passwords, private keys or proprietary SOLIDWORKS DLLs. Use a small synthetic reproduction. Do not change contour tolerances or weaken checks merely to make a test pass. Preserve native SOLIDWORKS geometry and never overwrite existing project output.

For profile cutting, the current algorithm is the accepted baseline. Discuss changes and provide geometry regressions before modifying it.

By submitting original contributions you agree to license them under this project's GPL-3.0-or-later terms and the SOLIDWORKS interoperability permission in LICENSE-NOTICE.md. Preserve third-party notices. Only contribute code you have the right to share.

Contributors can review and propose changes; publication of an official release remains a maintainer decision. No contributor is granted repository write access automatically.
