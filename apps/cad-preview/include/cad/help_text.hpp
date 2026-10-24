#pragma once
namespace cad {
struct HelpText {
    const wchar_t *title, *show, *hide, *menu;
    const wchar_t *actions[5]; // Rotate, zoom, pan, fit, menu.
    const wchar_t *inputs[5];
    const wchar_t *note;
};
const HelpText &helpText();
} // namespace cad
