#include "cad/help_text.hpp"
namespace cad {
const HelpText &helpText() {
    static const HelpText english{L"Controls",
                                  L"Show controls",
                                  L"Hide -",
                                  L"Controls help",
                                  {L"Rotate", L"Zoom", L"Pan", L"Fit to window", L"View menu"},
                                  {L"Left button + drag", L"Scroll wheel", L"Middle button + drag",
                                   L"Double middle-click / F", L"Right button"},
                                  L"Double middle-click to fit the whole model"};
    return english;
}
} // namespace cad