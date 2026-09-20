# Procurement queue row click behavior

Baseline: `5f6b18bf3ce77f2304580042fb182de0c48142b2`  
Branch: `codex/procurement-row-click-behavior`

## Interaction contract

- The PropertyCase title in the procurement queue is a canonical link to `/procurement/{CaseId}`.
- Clicking the title opens the full PropertyCase card.
- Clicking any other non-interactive part of the row keeps the existing fast-preview behavior and opens the right drawer.
- The title link stops click propagation, so opening the full card does not also trigger the drawer.
- The title remains a real anchor, preserving browser-native open-in-new-tab behavior.

## Scope

UI-only interaction change. No backend contracts, persistence, migrations, permissions or Procurement workflow semantics were changed.

## Test prepared

The existing Phase 1 browser scenario now verifies both paths:

1. clicking the business-number/sub area opens the procurement drawer without navigation;
2. clicking the title link navigates to the full canonical PropertyCase route.

Actual browser test execution remains with the repository verification workflow.
