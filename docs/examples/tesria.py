"""A small Tesria client: list spaces, search, write a page, label it.

Needs Python 3.9 or later and nothing else:  python3 tesria.py
"""
import json
import os
import urllib.error
import urllib.parse
import urllib.request

BASE = os.environ["TESRIA_URL"]      # such as https://wiki.example.com
TOKEN = os.environ["TESRIA_TOKEN"]   # starts with cct_
SPACE = os.environ["TESRIA_SPACE"]   # a space key, such as TEAM


class TesriaError(Exception):
    def __init__(self, method, path, status, data):
        self.status = status
        self.data = data
        code = (data or {}).get("code") or (data or {}).get("title") or ""
        super().__init__(f"{method} {path}: {status} {code}")


def tesria(method, path, body=None, params=None):
    url = BASE + path + ("?" + urllib.parse.urlencode(params) if params else "")
    request = urllib.request.Request(url, method=method)
    request.add_header("Authorization", f"Bearer {TOKEN}")
    data = None
    if body is not None:
        request.add_header("Content-Type", "application/json")
        data = json.dumps(body).encode()
    try:
        with urllib.request.urlopen(request, data) as response:
            text = response.read()
            return json.loads(text) if text else None
    except urllib.error.HTTPError as error:
        try:
            details = json.loads(error.read())
        except ValueError:
            details = None
        raise TesriaError(method, path, error.code, details) from None


def paragraph(text):
    return {"type": "paragraph", "content": [{"type": "text", "text": text}]}


def document_of(*blocks):
    return json.dumps({"type": "doc", "content": list(blocks)})


# 1. Which spaces can this token see?
for space in tesria("GET", "/api/spaces"):
    print(space["key"], space["name"])

# 2. Search, in one space.
space_id = tesria("GET", f"/api/spaces/{SPACE}")["id"]
for hit in tesria("GET", "/api/search", params={"q": "release", "spaceId": space_id}):
    print(hit["title"], hit["pageId"])

# 3. Write a page.
page = tesria("POST", "/api/pages", {
    "spaceId": space_id,
    "title": "Release 1.4 notes",
    "contentJson": document_of(paragraph("Written by a script.")),
})
print("created", page["id"], "version", page["currentVersionNumber"])


# 4. Change it without overwriting anyone: send the version you read.
def append(page_id, text):
    for _ in range(3):
        current = tesria("GET", f"/api/pages/{page_id}")
        doc = json.loads(current["contentJson"])
        doc["content"].append(paragraph(text))
        try:
            return tesria("PUT", f"/api/pages/{page_id}", {
                "title": current["title"],
                "contentJson": json.dumps(doc),
                "changeComment": "Added a line",
                "baseVersion": current["currentVersionNumber"],
            })
        except TesriaError as error:
            if error.status != 409:  # 409: someone else saved first, so read it again
                raise
    raise RuntimeError("The page kept changing; try again later.")


updated = append(page["id"], "And a second line.")
print("updated to version", updated["currentVersionNumber"])

# 5. Label it.
tesria("POST", f"/api/pages/{page['id']}/labels", {"name": "release-notes"})
print("labeled")
