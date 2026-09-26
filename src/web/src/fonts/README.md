# The wordmark font

`tesria-wordmark.woff2` is [Archivo](https://github.com/Omnibus-Type/Archivo)
(the font tesria.com uses for its wordmark), cut down to the six letters of
TESRIA, with its weight and width axes kept, and renamed "Tesria Wordmark"
as the SIL Open Font License 1.1 asks of a modified version. It is 3.5 KB,
and `index.css` embeds it as a data URI, so the app, exported sites and the
`/trust` page all carry it.

Archivo is Copyright 2020 The Archivo Project Authors, under the SIL Open
Font License 1.1. The `@fontsource-variable/archivo` package stays a
dependency of the web app for two reasons: its license travels into the
third-party notices with every other package's, and the cut can be made
again from it:

```bash
pyftsubset node_modules/@fontsource-variable/archivo/files/archivo-latin-wdth-normal.woff2 \
  --text=TESRIA --flavor=woff2 --layout-features=kern --no-hinting --desubroutinize \
  --output-file=src/fonts/tesria-wordmark.woff2
```

(`pip install fonttools brotli` provides `pyftsubset`.) Then replace the
base64 in the `@font-face` at the top of `src/index.css`.
