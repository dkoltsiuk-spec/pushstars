// Pure rules of in-app purchases (purchases.js reads and writes; purchases.test.js pins these).
// The store sells what ShopScreen.GemAmounts and AvatarCatalog list; keep the product ids identical.
const PRODUCTS = {
  gems_30: { gems: 30 },
  gems_120: { gems: 120 },
  gems_360: { gems: 360 },
  hero_skinny: { hero: 'skinny' },
  hero_chubby: { hero: 'chubby' },
  hero_skeleton: { hero: 'skeleton' },
};

const productOf = id => (Object.prototype.hasOwnProperty.call(PRODUCTS, id) ? PRODUCTS[id] : null);

// What this account gets for a verified transaction, given who claimed it before.
//  - Gems are consumable: one transaction pays one account, once. The same account asking again
//    (the answer got lost, a reinstall) is told again, and the phone's ledger will not pay twice.
//  - A hero is forever: restoring on another account of the same Apple ID hands it over too.
function grantFor(productId, claim, uid) {
  const product = productOf(productId);
  if (!product) return { status: 'unknown-product', gems: 0, hero: '', isNew: false };
  const mine = !claim || claim.uid === uid;
  if (product.hero) return { status: 'ok', gems: 0, hero: product.hero, isNew: !claim };
  return mine
    ? { status: 'ok', gems: product.gems, hero: '', isNew: !claim }
    : { status: 'claimed', gems: 0, hero: '', isNew: false };
}

// What a verified transaction may no longer do: refunded or cancelled by Apple.
const isRevoked = transaction => Number.isFinite(transaction?.revocationDate) && transaction.revocationDate > 0;

module.exports = { PRODUCTS, productOf, grantFor, isRevoked };
