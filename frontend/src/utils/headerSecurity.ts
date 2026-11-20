export function isSensitiveHeaderName(name: string): boolean {
  const compact = name.replace(/[^a-z0-9]/gi, '').toLowerCase()
  return ['auth', 'token', 'secret', 'password', 'credential', 'cookie', 'session', 'apikey', 'signature'].some(marker => compact.includes(marker)) || /(?:^|[-_])key(?:$|[-_])/i.test(name)
}