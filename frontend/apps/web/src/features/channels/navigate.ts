/** Leaves the dashboard for an external page (Facebook Login). Its own module so tests can replace it. */
export function leaveTo(url: string) {
  window.location.assign(url);
}
