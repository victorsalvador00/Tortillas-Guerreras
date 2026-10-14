import Constants from 'expo-constants';

const extra = (Constants.expoConfig?.extra ?? {}) as { apiUrl?: string; portalUrl?: string };

/** URL del API: `EXPO_PUBLIC_API_URL` (perfiles de EAS) o `extra.apiUrl` de app.json. */
export const API_URL = (process.env.EXPO_PUBLIC_API_URL ?? extra.apiUrl ?? 'http://10.0.2.2:5080').replace(/\/$/, '');

/** Portal de autofactura: el QR del ticket apunta a `<portal>/f/<folio>` (07-web). */
export const PORTAL_URL = (process.env.EXPO_PUBLIC_PORTAL_URL ?? extra.portalUrl ?? 'http://10.0.2.2:5174').replace(/\/$/, '');

export const VERSION_APP = Constants.expoConfig?.version ?? '1.0.0';

export function urlTicket(folio: string): string {
  return `${PORTAL_URL}/f/${encodeURIComponent(folio)}`;
}
