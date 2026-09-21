# Local Parser photo gallery — implementation report

**Scope:** local Parser Agent listing details UI only
**Parser extraction / Server:** unchanged

## Implemented

- Replaced the cropped 180 px photo strip with a 270–360 px preview using `Stretch=Uniform`, so the whole image remains visible.
- Added previous/next navigation buttons.
- Added `Фото N из M` position indicator.
- Added a horizontally scrollable thumbnail strip; selecting a thumbnail changes the main photo.
- Clicking the main preview opens a dedicated large photo viewer.
- The large viewer supports previous/next buttons, thumbnail selection, Left/Right keyboard navigation and Esc to close.
- Returning from the large viewer keeps the photo selected there.
- Added a neutral `Фотографии не найдены` placeholder when a listing has no photos.
- The same gallery automatically works in both the side panel and the existing separate listing-details window because both use `ListingDetailsControl`.

## Deliberately unchanged

- source adapters and photo extraction;
- local observation storage;
- Server / Collector contracts;
- listing business fields.

## Test coverage added

- the details gallery exposes preview, navigation buttons, thumbnails and empty state;
- the main preview is fixed to `Stretch.Uniform` so future UI changes do not reintroduce cropping.

Build/tests are left for the owner according to the agreed workflow.