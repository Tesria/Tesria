import { describe, expect, it } from 'vitest'
import { extractWordMarkers, markerKind, msoListInfo } from './wordPaste'

// The DOM half (building the lists) needs a browser and is checked live;
// these are the string rules it rests on (t4-021).

describe('extractWordMarkers', () => {
  it('replaces the older conditional-comment marker with its text', () => {
    const html = `<p style='mso-list:l0 level1 lfo1'><![if !supportLists]><span style='font-family:Symbol'>·<span style='font:7.0pt "Times New Roman"'>&nbsp;&nbsp; </span></span><![endif]>First bullet<o:p></o:p></p>`
    expect(extractWordMarkers(html)).toBe(
      `<p style='mso-list:l0 level1 lfo1'><span data-word-marker="·"></span>First bullet<o:p></o:p></p>`,
    )
  })

  it('handles the newer comment spelling and numbered markers', () => {
    const html = `<p><!--[if !supportLists]--><span style='mso-list:Ignore'>12.<span>&nbsp;&nbsp;</span></span><!--[endif]-->Twelfth</p>`
    expect(extractWordMarkers(html)).toBe(`<p><span data-word-marker="12."></span>Twelfth</p>`)
  })

  it('treats every marker block separately', () => {
    const html = `<![if !supportLists]>a)<![endif]>One <![if !supportLists]>b)<![endif]>Two`
    expect(extractWordMarkers(html)).toBe(
      `<span data-word-marker="a)"></span>One <span data-word-marker="b)"></span>Two`,
    )
  })

  it('leaves HTML without Word markers alone', () => {
    const html = '<ul><li>Google Docs</li></ul><!-- a comment -->'
    expect(extractWordMarkers(html)).toBe(html)
  })

  it('escapes a quote in a marker so the attribute stays whole', () => {
    expect(extractWordMarkers(`<![if !supportLists]>"<![endif]>x`)).toBe(`<span data-word-marker="&quot;"></span>x`)
  })
})

describe('msoListInfo', () => {
  it('reads the list and the level', () => {
    expect(msoListInfo("text-indent:-.25in;mso-list:l0 level1 lfo1")).toEqual({ list: 'l0', level: 1 })
    expect(msoListInfo('margin-left:1in; mso-list: l3 level2 lfo4')).toEqual({ list: 'l3', level: 2 })
  })

  it('is null for an ordinary paragraph or the ignore marker', () => {
    expect(msoListInfo('margin:0in')).toBeNull()
    expect(msoListInfo(null)).toBeNull()
    expect(msoListInfo('mso-list:Ignore')).toBeNull()
  })
})

describe('markerKind', () => {
  it('reads bullets as a bulleted list', () => {
    for (const m of ['·', 'o', '§', '-', '', '•']) expect(markerKind(m)).toEqual({ ordered: false, start: 1 })
  })

  it('reads numbers and letters as a numbered list', () => {
    expect(markerKind('1.')).toEqual({ ordered: true, start: 1 })
    expect(markerKind('3)')).toEqual({ ordered: true, start: 3 })
    expect(markerKind('(2)')).toEqual({ ordered: true, start: 2 })
    expect(markerKind('a.')).toEqual({ ordered: true, start: 1 })
    expect(markerKind('iv.')).toEqual({ ordered: true, start: 1 })
  })
})
