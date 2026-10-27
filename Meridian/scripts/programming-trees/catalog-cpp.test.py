"""Focused evidence and classification checks for the source-derived inventory."""
from pathlib import Path
import json, hashlib, re, csv

ROOT=Path(__file__).resolve().parents[2]
FOLDER=ROOT/'content/programming-trees-20261021'
data=json.loads((FOLDER/'cpp-types.json').read_text(encoding='utf-8'))
types={r['id']:r for r in data['types']}
assert len(types)==len(data['types'])==965
ids=[]
for category in data['categories']:
    assert category['count']==sum(group['count'] for group in category['children'])
    for group in category['children']:
        assert group['count']==len(group['typeIds'])
        for ident in group['typeIds']:
            ids.append(ident)
            assert types[ident]['categoryPath']==[category['id'],group['id']]
assert len(ids)==len(set(ids))==len(types)
assert set(ids)==set(types)

for item in data['provenance']['files']:
    raw=(FOLDER/'sources/cpp-n4950'/item['file']).read_bytes()
    assert hashlib.sha256(raw).hexdigest()==item['sha256'],item['file']
for r in types.values():
    assert r['qualifiedName'].startswith('std::')
    assert '__EXPOSITION__' not in r['qualifiedName']
    assert r['primaryHeader'] in r['headers'],r['qualifiedName']
    assert r['purpose'] and '__EXPOSITION__' not in r['purpose'],r['qualifiedName']
    source=(FOLDER/'sources/cpp-n4950'/r['sourceFile']).read_text(encoding='utf-8').splitlines()
    actual=source[r['sourceLine']-1]
    assert r['name'] in actual,(r['qualifiedName'],actual)
    assert r['sourceUrl'].endswith('#L'+str(r['sourceLine']))

# Guard common parser omissions: LaTeX-wrapped public names, digit-separated
# code-block environments, nested support types, aliases and deprecated types.
for name in ['std::vector','std::string','std::ratio','std::yotta','std::ratio_equal',
             'std::format_string','std::ranges::zip_view','std::ranges::keys_view',
             'std::ranges::cartesian_product_view','std::ranges::views::all_t',
             'std::mdspan','std::flat_map','std::expected','std::generator',
             'std::thread::id','std::locale::facet','std::filesystem::path::iterator',
             'std::aligned_storage_t','std::wstring_convert','std::pmr::vector']:
    assert name in types,name
assert types['std::vector']['categoryPath'][0]=='containers'
assert types['std::hash']['categoryPath'][0]=='functions'
assert types['std::formatter']['categoryPath'][0]=='text'
assert types['std::atomic']['categoryPath'][0]=='concurrency'
assert types['std::tuple_size']['categoryPath'][0]=='values'
assert types['std::size_t']['categoryPath']==['numeric','numeric/integer']
assert types['std::common_type']['categoryPath']==['meta','meta/transforms']
assert types['std::type_identity']['categoryPath']==['meta','meta/transforms']
assert types['std::basic_string']['categoryPath']==['text','text/strings']
assert types['std::char_traits']['categoryPath']==['text','text/characters']
assert types['std::tuple']['primaryHeader']=='<tuple>'
for name,cat in [('std::pmr::vector','containers'),('std::pmr::string','text'),
                 ('std::pmr::memory_resource','memory'),('std::pmr::generator','concurrency'),
                 ('std::thread','concurrency'),('std::mutex','concurrency'),
                 ('std::is_error_code_enum','diagnostics')]:
    assert types[name]['categoryPath'][0]==cat,name
assert types['std::aligned_storage_t']['status']=='C++23; deprecated'
assert types['std::wstring_convert']['categoryPath'][0]=='compatibility'
assert data['coverage']['headerSynopsisCount']==111
assert data['coverage']['headersWithTypeDeclarations']==96
assert sum(data['coverage']['kinds'].values())==965
with (FOLDER/'Cpp-standard-library-types.csv').open(encoding='utf-8-sig',newline='') as stream:
    rows=list(csv.DictReader(stream))
assert len(rows)==965
assert set(r['Qualified name'] for r in rows)==set(types)
assert all(r['Header'] and r['Purpose'] and r['Status'] and r['Category path'] and r['Source URL'] for r in rows)
print('C++ catalogue verified: 965 unique type records, exact source evidence, exhaustive category membership, parser regression samples.')
