import { HttpContextToken } from '@angular/common/http';

/** Personal playback requests may expire without navigating away from a public video. */
export const BACKGROUND_HISTORY_REQUEST = new HttpContextToken(() => false);
