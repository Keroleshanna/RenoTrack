import { Role } from '../../core/auth/auth';
import { customerCapabilitiesFor } from './customer-capabilities';

/**
 * `PermissionMatrix.md` §5: viewing and correcting a customer's address are Admin `F` and Inspector
 * `—` (D112). Every role the Dashboard knows is asserted, plus the signed-out case, because a
 * permission table goes wrong where it says *no*.
 */
describe('Customer capabilities', () => {
  it('lets Verwaltung (Admin) correct a customer address', () => {
    expect(customerCapabilitiesFor('admin').canCorrectAddress).toBeTrue();
  });

  it('gives Bauleitung (Inspector) no access to the address at all', () => {
    expect(customerCapabilitiesFor('inspector').canCorrectAddress).toBeFalse();
  });

  it('fails secure when no role is established', () => {
    expect(customerCapabilitiesFor(null).canCorrectAddress).toBeFalse();
  });

  it('grants the action to exactly one role', () => {
    const roles: readonly Role[] = ['admin', 'inspector'];

    expect(roles.filter((role) => customerCapabilitiesFor(role).canCorrectAddress)).toEqual(['admin']);
  });
});
