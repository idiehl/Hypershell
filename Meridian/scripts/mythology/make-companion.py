"""Build the five-page, original reader/test companion with ReportLab.

Run with the Codex bundled Python, or any Python with reportlab installed.
The output is a printable guide, not a statement that its test cases passed.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
from xml.sax.saxutils import escape

from reportlab.lib import colors
from reportlab.lib.enums import TA_LEFT
from reportlab.lib.styles import ParagraphStyle
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.pdfgen import canvas
from reportlab.platypus import Paragraph


ROOT = Path(__file__).resolve().parents[2]
CONTENT = ROOT / "content" / "greek-mythology"
WIDTH, HEIGHT = 612, 792
MARGIN = 46
INK = colors.HexColor("#182B3A")
TEAL = colors.HexColor("#116F72")
GOLD = colors.HexColor("#B78B3A")
PAPER = colors.HexColor("#FAF8F1")
MUTED = colors.HexColor("#52636B")
RULE = colors.HexColor("#CDD5D2")
PALE = colors.HexColor("#EBF1ED")


def fonts():
    folder = Path("C:/Windows/Fonts")
    available = [
        ("Body", "segoeui.ttf"), ("BodyBold", "segoeuib.ttf"),
        ("Display", "georgia.ttf"), ("DisplayBold", "georgiab.ttf"),
    ]
    for name, filename in available:
        path = folder / filename
        if path.exists():
            pdfmetrics.registerFont(TTFont(name, str(path)))
        else:
            fallback = {"Body": "Helvetica", "BodyBold": "Helvetica-Bold",
                        "Display": "Times-Roman", "DisplayBold": "Times-Bold"}[name]
            pdfmetrics.registerFont(pdfmetrics.Font(name, fallback, "WinAnsiEncoding"))
    pdfmetrics.registerFontFamily("Body", normal="Body", bold="BodyBold", italic="Body", boldItalic="BodyBold")


class Guide:
    def __init__(self, output: Path):
        output.parent.mkdir(parents=True, exist_ok=True)
        self.pdf = canvas.Canvas(str(output), pagesize=(WIDTH, HEIGHT), pageCompression=1)
        self.pdf.setTitle("Greek Mythology - Reader and Test Companion")
        self.pdf.setAuthor("Atlas Meridian")
        self.pdf.setSubject("Reading routes, literary boundaries and repeatable Meridian observations")
        self.page = 0
        self.lowest = {}

    def para(self, text, x, top, width, size=10.1, leading=14.1, color=INK, bold=False):
        style = ParagraphStyle("p", fontName="BodyBold" if bold else "Body", fontSize=size,
                               leading=leading, textColor=color, alignment=TA_LEFT,
                               spaceAfter=0, allowWidows=0, allowOrphans=0)
        p = Paragraph(text, style)
        _, height = p.wrap(width, HEIGHT)
        bottom = top - height
        if bottom < 53:
            raise ValueError(f"Page {self.page}: content descends into footer ({bottom:.1f})")
        p.drawOn(self.pdf, x, bottom)
        self.lowest[self.page] = min(self.lowest.get(self.page, HEIGHT), bottom)
        return bottom

    def label(self, text, x, y, color=TEAL, size=8.7):
        self.pdf.setFillColor(color)
        self.pdf.setFont("BodyBold", size)
        self.pdf.drawString(x, y, text)

    def rule(self, y, x=MARGIN, width=WIDTH - 2 * MARGIN):
        self.pdf.setStrokeColor(RULE)
        self.pdf.setLineWidth(.65)
        self.pdf.line(x, y, x + width, y)

    def new_page(self, section, title, subtitle):
        if self.page:
            self.pdf.showPage()
        self.page += 1
        self.pdf.setFillColor(PAPER)
        self.pdf.rect(0, 0, WIDTH, HEIGHT, fill=1, stroke=0)
        self.label("ATLAS MERIDIAN", MARGIN, 755, INK)
        self.label(section.upper(), 349, 755, MUTED, 8.2)
        self.rule(742)
        self.pdf.setFont("Display", 27)
        self.pdf.setFillColor(INK)
        self.pdf.drawString(MARGIN, 704, title)
        self.para(subtitle, MARGIN, 683, 515, 10.4, 14.6, MUTED)
        self.rule(43)
        self.label("GREEK MYTHOLOGY  /  READER + TEST COMPANION", MARGIN, 27, MUTED, 7.1)
        self.label(f"{self.page:02d} / 05", 527, 27, TEAL, 8)

    def card(self, number, title, text, top, height=82):
        self.pdf.setFillColor(PALE)
        self.pdf.roundRect(MARGIN, top - height, 520, height, 7, fill=1, stroke=0)
        self.label(number, 61, top - 25, TEAL, 13)
        self.label(title, 100, top - 23, INK, 11.1)
        self.para(text, 100, top - 33, 446, 9.8, 13.5)

    def step(self, number, title, text, top, width=492):
        self.label(number, MARGIN, top - 11, TEAL, 9.4)
        self.label(title, 74, top - 11, INK, 10.2)
        end = self.para(text, 74, top - 19, width, 9.4, 12.6)
        return end - 13

    def finish(self):
        if self.page != 5:
            raise ValueError(f"Expected five pages, got {self.page}")
        self.pdf.save()


def make(output: Path):
    fonts()
    # Read the collection so a missing or malformed source corpus fails early.
    data = {name: json.loads((CONTENT / name).read_text(encoding="utf-8"))
            for name in ("gods.json", "epics.json", "hesiod-rome.json", "fixtures.json")}
    sources = {s["id"]: s for corpus in (data["gods.json"], data["epics.json"], data["hesiod-rome.json"])
               for s in corpus["sources"]}
    guide = Guide(output)
    g, c = guide, guide.pdf

    # 1. Reading routes, rather than an ornamental cover.
    g.new_page("01  /  Choose a route", "Greek mythology, in context",
               "A companion for reading the collection and observing how Meridian behaves. "
               "Start with a question, follow a named source, then record what you find.")
    g.label("ONE COLLECTION. SEVERAL WAYS THROUGH IT.", MARGIN, 627, TEAL)
    g.card("01", "Origins and relationships", "Begin here &gt; Origins and Titans &gt; Olympian family tree &gt; a god's dossier. "
           "Read the parentage note before treating a line as universal. Follow partners and descendants as narrated relationships.", 611, 88)
    g.card("02", "Epic, one book at a time", "Open The Iliad or The Odyssey. Use the overview, then the six four-book canvases. "
           "Keep the speaker, time of narration and remembered events distinct. A reading order is not a modern geographic itinerary.", 511, 88)
    g.card("03", "Hesiod's two questions", "Read Theogony for origins and the settlement of divine power; read Works and Days "
           "for labor, justice and human vulnerability. Compare Prometheus and Pandora across the two works without merging their details.", 411, 88)
    g.card("04", "Greek and Roman comparisons", "Move from a Greek dossier to Roman gods and counterparts, then Roman ritual and household. "
           "Ask what a pairing explains and what it leaves out. Mars, Vesta, Liber and the Lares expose the limits of a rename table.", 311, 88)
    g.card("05", "Objects, files and experiments", "Visit Museum image gallery, Documents and media tests, and References and Boolean lab. "
           "An artifact has a date and provenance; a test fixture has an expected behavior. Neither supplies missing ancient evidence.", 211, 88)
    g.para("<b>Navigation:</b> Fit gives an overview; zoom in to read. Use the canvas list or launch cards. "
           "Download long TXT attachments for complete reading; their previews are limited. This guide is original editorial material, not an ancient source.",
           MARGIN, 106, 520, 9.4, 13.1, MUTED)

    # 2. Source method and compact, source-linked literary boundaries.
    g.new_page("02  /  Read the evidence", "Keep the versions visible",
               "Myth is a set of tellings, rituals and images transmitted through time. "
               "A clear comparison preserves those differences instead of manufacturing one seamless canon.")
    xs = [46, 224, 402]
    for x, title, desc in zip(xs, ["PASSAGE", "CONTEXT", "COMPARISON"],
                             ["Work + book or lines", "Speaker + genre + date", "Agreement + difference"]):
        c.setFillColor(PALE)
        c.roundRect(x, 572, 164, 55, 6, fill=1, stroke=0)
        g.label(title, x + 12, 606)
        g.para(desc, x + 12, 595, 144, 9.2, 12)
    for x in (213, 391):
        c.setStrokeColor(TEAL)
        c.setLineWidth(1)
        c.line(x - 1, 600, x + 8, 600)
        c.line(x + 4, 604, x + 8, 600)
        c.line(x + 4, 596, x + 8, 600)
    y = 551
    entries = [
        ("The Iliad has a deliberate boundary", "It begins with Achilles' anger and ends with Hector's funeral. "
         "The fall of Troy and Achilles' death are not its narrated ending. Locate remembered or anticipated events inside the speaker's account. [1]"),
        ("The Odyssey changes narrative level", "Books 9-12 are Odysseus' retrospective story to the Phaeacians. "
         "The poem's book order therefore differs from the travel sequence within that speech. The wooden horse is recalled in Books 4 and 8. [2]"),
        ("The two Hesiodic poems do different work", "The Theogony's first woman is unnamed; Works and Days names Pandora "
         "and tells the jar episode. Its five human races include the heroic race. Do not reduce this to Ovid's four-metal sequence. [3, 4]"),
        ("A Roman counterpart is an argument", "A shared story or attribute can support an identification without erasing a local cult. "
         "Compare the named poet, rite or object: Ovid's literary Mars and Cato's farm prayer answer different questions. [4, 5]"),
    ]
    for i, (title, text) in enumerate(entries, 1):
        y = g.step(f"0{i}", title, text, y)
    g.rule(y + 2)
    y -= 14
    y = g.para("<b>Before drawing a conclusion</b><br/>Record the work and ancient locator; distinguish a character's statement from "
               "the narrator's; preserve alternative parentage as a variant; identify coercion when a story describes it. "
               "For an image, record its museum number, date, culture and proposed identification. Later reception is evidence of later interpretation.",
               MARGIN, y, 520, 9.4, 12.8) - 14
    g.label("READING ANCHORS  /  FULL REFERENCES LIVE IN THE SOURCE REGISTER", MARGIN, y, TEAL, 7.8)
    def cite(label, source_id):
        return f'<a href="{escape(sources[source_id]["url"])}" color="#116F72">{escape(label)}</a>'

    anchors = [
        "[1] Homer, " + cite("Iliad 1", "iliad-01") + " and " + cite("24", "iliad-24"),
        "[2] Homer, Odyssey " + ", ".join(cite(str(i), f"odyssey-{i:02d}") for i in (4, 8, 9, 10, 11, 12)),
        "[3] Hesiod, " + cite("Theogony 535-616", "hr-theogony") + "; " + cite("Works and Days 42-201", "hr-works"),
        "[4] Ovid, " + cite("Metamorphoses 1.89-150", "hr-met1") + "; " + cite("Fasti 3.1-42", "hr-fasti3"),
        "[5] Cato, " + cite("On Agriculture 141.1-4", "hr-cato141"),
    ]
    y -= 10
    for entry in anchors:
        y = g.para(entry, MARGIN, y, 520, 8.4, 11.6) - 2

    # 3. Functional checks with named collection nodes and a full Boolean table.
    g.new_page("03  /  Exercise the lab", "A small, repeatable experiment",
               "Use a working copy in the editor for save tests. The Lab is an explicitly invented "
               "Boolean model of voyage preparation; it does not compute the meaning of a myth.")
    y = 629
    steps = [
        ("Run across canvases", "Choose Run, change canvas, then return to the Lab. Run and Stop apply across all canvases "
         "in this viewer instance. Stop must preserve positions and wiring. A publication initially opens stopped."),
        ("Compare live and saved", "Toggle Crew prepared: its live mirror follows immediately. In the editor, change Archive reviewed "
         "on Begin here without saving. The Lab's saved reference stays unchanged until a successful explicit save. Viewer controls cannot refresh that snapshot."),
        ("Observe Unknown", "Open the Enable voyage switch: Voyage ready becomes Unknown. Close it to restore evaluation. "
         "The deliberately unwired output remains Unknown while running. Missing information is not false."),
        ("Collapse, then inspect", "Record Voyage ready. Collapse Voyage preparation: its children hide while the sibling output stays "
         "visible with its computed result. Expand again and verify the wiring and inputs remain. Collapse changes presentation, not the circuit."),
    ]
    for i, (title, text) in enumerate(steps, 1):
        y = g.step(f"0{i}", title, text, y)
    g.label("05  CHECK EVERY DEFINED INPUT COMBINATION", MARGIN, y - 2, TEAL, 8.7)
    g.para("While running, compare the seven exercises with this table. T = true; F = false. "
           "NOT uses only A; the other gates use A and B.", MARGIN, y - 12, 520, 9.1, 12.3)
    top = y - 47
    columns = ["A", "B", "AND", "OR", "NOT A", "NAND", "NOR", "XOR", "XNOR"]
    rows = [columns, ["F", "F", "F", "F", "T", "T", "T", "F", "T"],
            ["F", "T", "F", "T", "T", "T", "F", "T", "F"],
            ["T", "F", "F", "T", "F", "T", "F", "T", "F"],
            ["T", "T", "T", "T", "F", "F", "F", "F", "T"]]
    widths = [37, 37, 63, 53, 68, 68, 60, 62, 72]
    for r, row in enumerate(rows):
        c.setFillColor(INK if r == 0 else (PALE if r % 2 else colors.white))
        c.rect(MARGIN, top - (r + 1) * 23, 520, 23, fill=1, stroke=0)
        x = MARGIN
        for val, width in zip(row, widths):
            c.setFillColor(colors.white if r == 0 else INK)
            c.setFont("BodyBold" if r == 0 else "Body", 8.8)
            c.drawCentredString(x + width / 2, top - r * 23 - 15, val)
            x += width
    y = top - 130
    g.para("<b>Optional editor checks:</b> attempt a functional cycle on a disposable copy and record the rejection. "
           "The Lab's Warning / Choice / Consequence loop uses visual links, so it remains valid. "
           "Do not convert a decorative relation into a signal accidentally.", MARGIN, y, 520, 9.3, 12.7)

    # 4. Media behavior and the two file lifecycles.
    g.new_page("04  /  Media and portability", "What travels, what previews",
               "Preserving original bytes and displaying a preview are separate capabilities. "
               "Use the labelled files in Documents and media tests to observe both.")
    table = [
        ("FORMAT", "EDITOR", "PUBLISHED VIEWER"),
        ("PNG / JPEG / WebP", "Native image", "Native image"),
        ("GIF (imported image)", "Supported image", "Supported image"),
        ("TXT", "Full text on open", "Text preview (3,000 chars)"),
        ("PDF", "Browser PDF preview*", "Browser PDF preview*"),
        ("Markdown (.md)", "Download fallback", "Text preview"),
        ("SVG / WAV / JSON", "Download only", "Download only"),
    ]
    top, rowh = 627, 29
    colx = [46, 215, 389]
    colw = [169, 174, 177]
    for r, row in enumerate(table):
        c.setFillColor(INK if r == 0 else (PALE if r % 2 else colors.white))
        c.rect(MARGIN, top - (r + 1) * rowh, 520, rowh, fill=1, stroke=0)
        for text, x, width in zip(row, colx, colw):
            g.para(escape(text), x + 10, top - r * rowh - 8, width - 16,
                   8.7 if r == 0 else 9.0, 11.5, colors.white if r == 0 else INK, r == 0)
    y = g.para("* PDF display depends on the browser. Original-file open/download remains the fallback. "
               "TXT node cards stop at 3,000 characters. The editor's Open attachment dialog shows the full TXT; "
               "the published viewer's TXT dialog stops at 3,000. Download the original for complete text anywhere. "
               "The WAV is an original test motif, not reconstructed ancient music. GIF support describes the imported image fixture.",
               MARGIN, 414, 520, 8.9, 12.0, MUTED) - 17
    y = g.step("01", "Round-trip an editable project", "Save a working copy, export .meridian, and import it. "
               "If its identity is already in the library, choose the explicit copy workflow. Compare canvas and attachment counts, "
               "then open representative images, text and PDF. For byte fidelity, compare original and re-exported asset hashes.", y)
    y = g.step("02", "Publish and open from disk", "Publish to a single .html file. Open it directly with the host stopped, "
               "then exercise navigation, exposed controls, Run/Stop and downloads. Also test it in the supplied iframe example "
               "at a narrow viewport. Publishing itself does not upload the collection.", y)
    y = g.step("03", "Check the external fixture", "Publish once with Download External References selected and once without. "
               "The pinned curator note should be included in the first and explicitly unavailable in the second. "
               "Owned project canvases still travel with both. This note is a test fixture, not a mythology source.", y)
    g.step("04", "Import the publication as new", "Import the HTML in the editor and confirm a fresh project identity. "
           "Included identities are remapped; published data is imported without executing its HTML. In the viewer, Reset restores "
           "authored values. Save a copy downloads the authored publication, not the visitor's temporary control changes.", y)

    # 5. A printable observation instrument with space for actual evidence.
    g.new_page("05  /  Record an observation", "Make the result reproducible",
               "Complete one sheet per reading question or behavior test. Record what actually happened; "
               "an unchecked expectation is a plan, not a passed test.")
    g.label("CASE ID / DATE", MARGIN, 621)
    g.label("EDITOR OR VIEWER / BROWSER + VERSION", 276, 621)
    g.rule(598, MARGIN, 204)
    g.rule(598, 276, 290)
    g.label("PROJECT REVISION / FILE NAME", MARGIN, 577)
    g.label("VIEWPORT / ZOOM / RUN STATE", 326, 577)
    g.rule(554, MARGIN, 252)
    g.rule(554, 326, 240)

    def field(label, hint, top, lines=2):
        g.label(label, MARGIN, top)
        g.para(hint, MARGIN, top - 9, 520, 8.2, 10.8, MUTED)
        for i in range(lines):
            g.rule(top - 42 - i * 20)
        return top - 42 - (lines - 1) * 20 - 25

    y = field("1  LOCATE THE EVIDENCE", "Canvas + node, or ancient work + book/lines. For an image, add museum object number and date.", 533, 2)
    y = field("2  WRITE THE STARTING STATE", "Input values, reference type, last explicit save, switch position, and any relevant file or asset hash.", y, 2)
    y = field("3  REPEAT THE ACTIONS", "Use a short numbered sequence. Change one variable at a time; identify when a save or reload occurred.", y, 2)
    y = field("4  COMPARE EXPECTED AND ACTUAL", "State the expected value or source claim, then the observed value, notice, preview, or difference. Attach evidence IDs.", y, 2)
    y = field("5  CLASSIFY THE RESULT", "Behavior: pass / fail / not observed. Reading: supported / variant / uncertain. Add the next discriminating check.", y, 1)
    g.para("<b>Evidence discipline:</b> distinguish a literary variant from an error, and a download fallback from data loss. "
           "For software, retain the smallest repeatable case. For interpretation, retain the exact passage and its context.",
           MARGIN, y + 4, 520, 9.0, 12.4, MUTED)
    g.finish()
    print(json.dumps({"output": str(output), "pages": g.page, "lowest_text_y": g.lowest,
                      "source_records_read": len(sources)}, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, default=CONTENT / "media" / "mythology-companion.pdf")
    make(parser.parse_args().output)
