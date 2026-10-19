# Web UI rules

- Follow the latest design system and UI guidance published by the project's Claude maintainers. Read `CLAUDE.md`, `AMES_Office_Web_화면참조.md`, and the applicable current MD/SYS reference screen before editing UI. Current business requirements override stale demo behavior.
- Use Radzen controls for interactive Web tables, buttons, tabs, inputs, dropdowns and date pickers. Do not introduce parallel hand-built HTML controls or a separate visual system.
- Reuse the existing MD/SYS patterns: `AmesPageHeader`, `ames-kpis`, `ames-card`, `master-list-toolbar`, `ames-rz-grid`, `ames-rz-input`, `ames-rz-btn`, and the common modal header/footer.
- Before changing navigation, inspect a current MD/SYS reference screen. Use the existing `AmesTabs` / `AmesTab` for genuinely distinct views; never substitute `RadzenSelectBar` or page-specific tab styling. Creation is normally a list-toolbar `Create` button opening the standard modal, not a separate Create tab. Preserve functional requirements without inventing a different UI pattern.
- Match reference sizes, spacing, paging, date formats, theme tokens, and dialog behavior. Default action buttons to `ButtonSize.Small`, form controls to `InputSize.Small`, and use `DateFmt` for dates.
- Keep nested detail grids content-sized with a bounded scroll area; do not stretch their rows through parent fixed-grid rules. Physical print layouts and spatial maps can retain document/spatial markup, while their interactive controls use Radzen.
- Make UI-only requests without changing stock, database schema or business rules. Preserve existing functional requirements and validation.
