// Clap push-up times ride along with a recorded set so its replay can land the clap as a hit.
// They are cosmetic: the score is the rep timeline alone. A malformed list is therefore dropped
// whole instead of failing the result or the recording it came with.
function cleanClapTimes(clapTimes, repTimes, duration) {
  if (!Array.isArray(clapTimes) || !Array.isArray(repTimes) || clapTimes.length > repTimes.length) return [];
  let previous = -1;
  for (const time of clapTimes) {
    if (typeof time !== 'number' || !Number.isFinite(time) || time < 0 || time > duration || time - previous < 0.4)
      return [];
    previous = time;
  }
  return clapTimes.slice();
}
module.exports = { cleanClapTimes };
