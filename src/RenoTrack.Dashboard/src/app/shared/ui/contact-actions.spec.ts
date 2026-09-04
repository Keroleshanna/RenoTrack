import { whatsAppNumber } from './contact-actions';

/**
 * Deriving a WhatsApp number from what an Admin typed into a Lead form.
 *
 * **A wrong number here does not fail visibly — it opens a chat with somebody else.** That is why
 * these tests care more about the refusals than the conversions: when the country cannot be
 * established, the action must disappear rather than guess.
 */
describe('WhatsApp number', () => {
  it('strips punctuation from an international number', () => {
    expect(whatsAppNumber('+49 151 23456789')).toBe('4915123456789');
    expect(whatsAppNumber('+49 (0)151 234-567')).toBe('490151234567');
  });

  it('replaces a German trunk prefix with the country code', () => {
    // 0176 … is how a German number is written domestically; wa.me needs 49176 … instead.
    expect(whatsAppNumber('0176 12345678')).toBe('4917612345678');
    expect(whatsAppNumber('0151/2345678')).toBe('491512345678');
  });

  it('treats a 00 prefix as the international prefix it is', () => {
    expect(whatsAppNumber('0049 151 2345678')).toBe('491512345678');
  });

  it('refuses a number with no country information rather than guessing one', () => {
    // 151 2345678 could be any country. Hiding the action leaves the plain call link, which works.
    expect(whatsAppNumber('151 2345678')).toBeNull();
  });

  it('refuses anything too short to be a phone number', () => {
    expect(whatsAppNumber('12345')).toBeNull();
    expect(whatsAppNumber('n/a')).toBeNull();
  });

  it('refuses an absent number', () => {
    expect(whatsAppNumber(null)).toBeNull();
    expect(whatsAppNumber(undefined)).toBeNull();
    expect(whatsAppNumber('   ')).toBeNull();
  });
});
