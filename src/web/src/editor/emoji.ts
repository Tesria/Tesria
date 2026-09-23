/**
 * The `:` suggestion list. Stored as the literal character, so nothing new
 * enters the schema, the export renderer or the search index: an emoji is
 * just text that happened to be typed with a picker.
 *
 * A curated list rather than a full Unicode table: the whole set is ~1,900
 * entries with several names each, which is a real payload to ship on every
 * page load for a feature whose job is "find the one you meant in three
 * keystrokes". These are the ones that actually appear in a wiki.
 */
export type Emoji = { char: string; name: string; keywords?: string[] }

export const EMOJI: Emoji[] = [
  { char: '👍', name: 'thumbsup', keywords: ['+1', 'yes', 'approve'] },
  { char: '👎', name: 'thumbsdown', keywords: ['-1', 'no'] },
  { char: '✅', name: 'check', keywords: ['done', 'tick', 'yes'] },
  { char: '❌', name: 'x', keywords: ['no', 'fail', 'cross'] },
  { char: '⚠️', name: 'warning', keywords: ['caution'] },
  { char: '🚨', name: 'alert', keywords: ['siren', 'urgent'] },
  { char: '🔥', name: 'fire', keywords: ['hot', 'urgent'] },
  { char: '🎉', name: 'tada', keywords: ['party', 'ship', 'celebrate'] },
  { char: '🚀', name: 'rocket', keywords: ['ship', 'launch', 'deploy'] },
  { char: '🐛', name: 'bug', keywords: ['defect', 'issue'] },
  { char: '💡', name: 'idea', keywords: ['bulb', 'suggestion'] },
  { char: '📝', name: 'memo', keywords: ['note', 'docs', 'write'] },
  { char: '📌', name: 'pin', keywords: ['pinned', 'important'] },
  { char: '🔗', name: 'link', keywords: ['url'] },
  { char: '🔒', name: 'lock', keywords: ['secure', 'private'] },
  { char: '🔑', name: 'key', keywords: ['secret', 'credential'] },
  { char: '⏱️', name: 'timer', keywords: ['perf', 'speed', 'latency'] },
  { char: '📈', name: 'chart', keywords: ['growth', 'up', 'metrics'] },
  { char: '📉', name: 'chartdown', keywords: ['down', 'drop'] },
  { char: '🧪', name: 'test', keywords: ['experiment', 'lab'] },
  { char: '🛠️', name: 'tools', keywords: ['build', 'fix', 'maintenance'] },
  { char: '🧹', name: 'broom', keywords: ['cleanup', 'chore'] },
  { char: '📦', name: 'package', keywords: ['release', 'dependency', 'box'] },
  { char: '🗑️', name: 'trash', keywords: ['delete', 'remove'] },
  { char: '❓', name: 'question', keywords: ['help', 'ask'] },
  { char: '❗', name: 'exclamation', keywords: ['important'] },
  { char: '👀', name: 'eyes', keywords: ['review', 'look', 'watching'] },
  { char: '🙏', name: 'pray', keywords: ['thanks', 'please'] },
  { char: '👏', name: 'clap', keywords: ['applause', 'nice'] },
  { char: '🤔', name: 'thinking', keywords: ['hmm', 'unsure'] },
  { char: '😀', name: 'smile', keywords: ['happy'] },
  { char: '😅', name: 'sweat_smile', keywords: ['phew'] },
  { char: '😬', name: 'grimace', keywords: ['awkward', 'yikes'] },
  { char: '🎯', name: 'target', keywords: ['goal', 'objective'] },
  { char: '⭐', name: 'star', keywords: ['favorite', 'favourite'] },
  { char: '❤️', name: 'heart', keywords: ['love'] },
  { char: '☕', name: 'coffee', keywords: ['break'] },
  { char: '🏗️', name: 'construction', keywords: ['wip', 'building'] },
  { char: '🧭', name: 'compass', keywords: ['direction', 'plan'] },
  { char: '🔍', name: 'search', keywords: ['find', 'magnify'] },
]

export function filterEmoji(query: string): Emoji[] {
  const q = query.toLowerCase()
  if (!q) return EMOJI.slice(0, 12)
  return EMOJI.filter((e) => e.name.includes(q) || e.keywords?.some((k) => k.includes(q))).slice(0, 12)
}
