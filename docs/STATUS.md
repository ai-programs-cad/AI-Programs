# Release status

This is an initial community-source candidate, not a claim that every combination of Windows and SOLIDWORKS has been certified.

PartCraft comes from the saved personal version, not the trial/activation branch. The previous workstation acceptance covered sheet metal, profile cutting and selected faces including pockets/slots. This repository does not contain the private models used for those checks. Licensing enforcement is absent in the community branch.

The current preparation checks are recorded in the separate Verification report. Installation/uninstallation on a clean Windows system, Explorer registration, actual macro behavior on other models and full application builds with another contributor's SDK remain release acceptance tasks.

Some support classes in PartCraft are legacy diagnostics. Their sample model allow-lists have synthetic paths rather than the author's private paths. They are not a general-purpose exporter entry point.

Original macro logic is preserved. Comments in old profile macros may use older terminology; refer to the actual cutting side/orientation required by the model and program UI rather than treating H as a universal model-height field.

The public repository is https://github.com/ai-programs-cad/AI-Programs . The initial source upload is being prepared; downloadable binary releases are separate from source publication.
