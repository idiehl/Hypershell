"""Build a source-derived, task-organized C++23 type catalogue.

Input is the unmodified n4950 source snapshot under content/.../sources.
This is a declaration inventory, not a C++ parser or a function catalogue.
"""
from pathlib import Path
import re, json, hashlib, datetime, csv

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'content/programming-trees-20261021'
SRC = OUT / 'sources/cpp-n4950'
FILES = ['support','diagnostics','memory','meta','utilities','strings','containers',
         'iterators','ranges','algorithms','numerics','time','locales','iostreams','regex','threads','future']
BASE = 'https://github.com/cplusplus/draft/blob/n4950/source/'
records = {}
headers = []

def sanitize(text):
    # Preserve newlines so source line locations remain exact.
    text = re.sub(r'//[^\n]*', '', text)
    text = re.sub(r'/\*.*?\*/', lambda m: '\n' * m[0].count('\n'), text, flags=re.S)
    text = re.sub(r'@\\(?:libglobal|libconcept|libtype)\{([^{}]+)\}@', r'\1', text)
    text = re.sub(r'@\\libmember\{([^{}]+)\}\{[^{}]+\}@', r'\1', text)
    text = re.sub(r'@[^@]*@', lambda m: '__EXPOSITION__' + '\n' * m[0].count('\n'), text, flags=re.S)
    text = re.sub(r'\\[A-Za-z]+(?:\{[^{}]*\})?', '', text)
    text = re.sub(r'"(?:\\.|[^"\\])*"', '""', text)
    return text

EVENT = re.compile(
    r'(?P<namespace>\b(?:inline\s+)?namespace\s+(?P<ns>[A-Za-z_]\w*(?:::[A-Za-z_]\w*)*)\s*\{)'
    r'|(?P<type>\b(?P<kind>enum\s+class|enum|class|struct)\s+(?P<name>[A-Za-z_]\w*)\s*(?=[;:{<]))'
    r'|(?P<alias>\busing\s+(?P<aliasname>[A-Za-z_]\w*)\s*=)'
    r'|(?P<brace>[{}])')

for stem in FILES:
    path = SRC / (stem + '.tex')
    text = path.read_text(encoding='utf-8')
    sections = list(re.finditer(r'^\\rSec\d\[([^]]+)\].*$', text, re.M))
    for si, sec in enumerate(sections):
        if 'synopsis' not in sec[0] or 'Header ' not in sec[0]:
            continue
        header_match = re.search(r'<([^>]+)>', sec[0])
        if not header_match:
            continue
        header = header_match[1]
        section_text = text[sec.end():sections[si+1].start() if si+1 < len(sections) else len(text)]
        blocks = list(re.finditer(r'\\begin\{codeblock(?:digitsep)?\}(.*?)\\end\{codeblock(?:digitsep)?\}', section_text, re.S))
        headers.append({'header':header,'section':sec[1],'file':stem+'.tex','line':text[:sec.start()].count('\n')+1})
        for block in blocks:
            code = sanitize(block[1])
            start = sec.end() + block.start(1)
            stack = []
            for ev in EVENT.finditer(code):
                if ev['namespace']:
                    stack.append(('namespace',ev['ns']))
                    continue
                if ev['brace']:
                    if ev['brace']=='{': stack.append(('other',''))
                    elif stack: stack.pop()
                    continue
                # Only namespace scope; class members and implementation details excluded.
                if not stack or any(k!='namespace' for k,n in stack): continue
                ns = '::'.join(n for k,n in stack)
                if not ns.startswith('std'): continue
                name = ev['name'] if ev['type'] else ev['aliasname']
                if '__EXPOSITION__' in name: continue
                qn = ns+'::'+name
                kind = 'alias' if ev['alias'] else ('enum' if ev['kind'].startswith('enum') else ev['kind'])
                line = text[:start].count('\n')+code[:ev.start()].count('\n')+1
                line_end = code.find('\n',ev.start())
                decl = code[ev.start():line_end if line_end>=0 else len(code)].strip()
                record = {'qualifiedName':qn,'name':name,'namespace':ns,'kind':kind,
                          'headers':['<'+header+'>'],'status':'C++23',
                          'sourceUrl':BASE+stem+'.tex#L'+str(line),
                          'referenceUrl':'https://timsong-cpp.github.io/cppwp/n4950/'+sec[1],
                          'sourceSection':sec[1],'sourceLine':line,'sourceFile':stem+'.tex',
                          'declaration':decl}
                if ev['alias']:
                    end = code.find(';',ev.end())
                    record['aliasOf'] = re.sub(r'\s+',' ',code[ev.end():end]).strip()
                if qn in records:
                    old = records[qn]
                    old['headers']=sorted(set(old['headers']+record['headers']))
                    # Prefer a declaration in its actual header to iosfwd and extension synopses.
                    if old['headers'][0]=='<iosfwd>' and header!='iosfwd':
                        record['headers']=old['headers']; records[qn]=record
                else: records[qn]=record

def write_json(path,data):
    path.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')

def supplemental(qn,header,stem,anchor,needle,purpose,kind='class',status='C++23'):
    text=(SRC/(stem+'.tex')).read_text(encoding='utf-8')
    begin=text.index('['+anchor+']')
    try: where=text.index(needle,begin)
    except ValueError: raise ValueError('Supplement source not found: '+qn+' / '+needle)
    line=text[:where].count('\n')+1
    records[qn]={'qualifiedName':qn,'name':qn.split('::')[-1],
        'namespace':'::'.join(qn.split('::')[:-1]),'kind':kind,'headers':['<'+header+'>'],
        'status':status,'sourceUrl':BASE+stem+'.tex#L'+str(line),
        'referenceUrl':'https://timsong-cpp.github.io/cppwp/n4950/'+anchor,
        'sourceSection':anchor,'sourceLine':line,'sourceFile':stem+'.tex',
        'purpose':purpose,'supplemental':True}

# Public nested types with distinct user roles, and deprecated names specified
# outside their modern header synopsis. Arbitrary per-container iterator and
# member aliases are deliberately not duplicated as independent catalogue leaves.
SUPPLEMENTS=[
 ('std::thread::id','thread','threads','thread.thread.id','class thread::id','Identity of a thread; comparable, printable and hashable.'),
 ('std::ios_base::failure','ios','iostreams','ios.failure','class ios_base::failure','Exception describing a stream operation failure.'),
 ('std::locale::facet','locale','locales','locale.facet','class locale::facet','Base class for reference-counted locale facets.'),
 ('std::locale::id','locale','locales','locale.id','class locale::id','Unique identifier for a locale facet type.'),
 ('std::filesystem::path::iterator','filesystem','iostreams','fs.path.itr','path::iterator','Bidirectional iterator over path components.'),
 ('std::float_denorm_style','limits','future','depr.numeric.limits.has.denorm','enum float_denorm_style','Legacy classification of denormalized floating-point support.','enum','C++23; deprecated'),
 ('std::is_pod','type_traits','future','depr.meta.types','struct is_pod','Legacy plain-old-data predicate; prefer explicit layout and triviality traits.','struct','C++23; deprecated'),
 ('std::aligned_storage','type_traits','future','depr.meta.types','struct aligned_storage','Legacy raw aligned-storage trait; prefer alignas and byte arrays.','struct','C++23; deprecated'),
 ('std::aligned_union','type_traits','future','depr.meta.types','struct aligned_union','Legacy storage trait for several candidate types.','struct','C++23; deprecated'),
 ('std::aligned_storage_t','type_traits','future','depr.meta.types','libglobal{aligned_storage_t}','Alias for the deprecated aligned_storage result type.','alias','C++23; deprecated'),
 ('std::aligned_union_t','type_traits','future','depr.meta.types','libglobal{aligned_union_t}','Alias for the deprecated aligned_union result type.','alias','C++23; deprecated'),
 ('std::iterator','iterator','future','depr.iterator','struct iterator','Legacy iterator base; declare required aliases directly instead.','struct','C++23; deprecated'),
 ('std::wstring_convert','locale','future','depr.conversions.general','class wstring_convert','Legacy stateful byte/wide-character conversion wrapper.','class','C++23; deprecated'),
 ('std::wbuffer_convert','locale','future','depr.conversions.general','class wbuffer_convert','Legacy stream-buffer character conversion adapter.','class','C++23; deprecated'),
]
for args in SUPPLEMENTS:
    supplemental(*args)

CATEGORIES = {
 'values':('Values, tuples & choices','Compose values, represent absence, select alternatives and return expected failures.'),
 'memory':('Memory & object lifetime','Choose ownership, allocation, lifetime management and reusable memory resources.'),
 'containers':('Containers & array views','Choose a sequence, lookup structure, adapter or non-owning array view.'),
 'iteration':('Iterators & lazy ranges','Traverse data, build lazy pipelines and inspect structured algorithm results.'),
 'text':('Text, formatting & patterns','Store text, format values, convert numbers and match regular expressions.'),
 'io':('Files, streams & locales','Read and write data, traverse filesystems and apply locale conventions.'),
 'concurrency':('Concurrency & asynchronous work','Run threads, coordinate shared state, exchange results and suspend computations.'),
 'time':('Time, calendars & time zones','Measure durations, identify instants and represent civil dates and time zones.'),
 'numeric':('Numbers, randomness & bits','Represent numeric quantities, generate distributions and work with numeric arrays.'),
 'meta':('Type inspection & metaprogramming','Inspect type properties, transform types and describe compile-time relationships.'),
 'functions':('Callables & execution','Store and adapt callables, compare values and select execution policies.'),
 'diagnostics':('Errors & diagnostics','Represent failures and inspect exception, source-location and stack information.'),
 'compatibility':('Deprecated compatibility','Recognize C++23 legacy facilities and prefer their supported modern alternatives.'),
}

GROUPS = {
 'products':('Products & tuple protocol','Combine heterogeneous values and inspect tuple-shaped types.'),
 'choices':('Optional, variant & expected','Represent optional values, alternatives and value-or-error results.'),
 'construction':('Construction tags','Select piecewise, in-place, range and allocation-aware construction.'),
 'owners':('Smart ownership','Choose exclusive or shared ownership and non-owning weak observation.'),
 'allocators':('Allocators & pointer traits','Customize storage allocation and pointer adaptation.'),
 'resources':('Polymorphic resources','Select run-time allocation strategies for pmr-aware objects.'),
 'allocation':('Allocation protocol','Control allocation failure, alignment and destruction.'),
 'sequence':('Sequence storage','Store indexed or linked ordered elements.'),
 'ordered':('Ordered lookup','Use comparator-ordered keys, unique membership or repeated keys.'),
 'hashed':('Hash lookup','Use hash-based keys, unique membership or repeated keys.'),
 'adapters':('Stacks, queues & flat adapters','Restrict access order or build sorted lookup over sequence storage.'),
 'arrayviews':('Array & multidimensional views','Describe contiguous or multidimensional storage without taking ownership.'),
 'tags':('Construction & layout policies','Select how a storage object is constructed or indexed.'),
 'cursor':('Iterator adapters','Adapt traversal, insertion, movement, const access or streams.'),
 'iteratortraits':('Iterator traits & tags','Describe iterator values, references, differences and traversal strength.'),
 'rangetraits':('Range traits & foundations','Describe range properties, borrowing and reusable view foundations.'),
 'views':('Lazy range views','Construct or transform a lazily traversed sequence.'),
 'algorithmresults':('Algorithm result records','Retain iterators, values and other outputs from range algorithms.'),
 'strings':('Owning strings & borrowed views','Choose owned character storage or a non-owning string view.'),
 'characters':('Character conventions','Describe character operations, encodings and multibyte state.'),
 'format':('Formatting protocol','Build checked format strings, format arguments and custom formatters.'),
 'regex':('Regular expressions','Compile patterns, retain captures and iterate matches or tokens.'),
 'numbertext':('Number conversion','Describe locale-independent numeric character conversions.'),
 'filesystem':('Filesystem paths & traversal','Represent paths, file metadata, permissions and directory traversal.'),
 'streambase':('Stream foundations','Describe stream state, positions and character buffering.'),
 'streamio':('Input & output streams','Read or write formatted and unformatted character data.'),
 'filestreams':('File streams','Read and write external files through stream interfaces.'),
 'stringstreams':('String & span streams','Read or write memory-backed character sequences.'),
 'syncstreams':('Synchronized streams','Buffer and emit coordinated output to a shared stream.'),
 'locales':('Locales & facets','Apply language-sensitive classification, conversion and formatting.'),
 'cfile':('C-compatible I/O','Use C-compatible file handles and position information.'),
 'threads':('Threads & cancellation','Run a thread and coordinate cooperative stop requests.'),
 'locks':('Mutexes & lock ownership','Protect shared state with locking and ownership wrappers.'),
 'coordination':('Waiting & coordination','Wait for conditions or coordinate bounded permits and phases.'),
 'atomics':('Atomic objects & memory order','Perform synchronized operations on indivisible shared objects.'),
 'futures':('Promises & future results','Connect asynchronous producers, tasks and result consumers.'),
 'coroutines':('Coroutine machinery','Represent resumable computations and suspension behavior.'),
 'durations':('Durations & instants','Represent a time interval or an instant relative to a clock.'),
 'clocks':('Clocks & clock conversion','Choose a time scale and convert between clock domains.'),
 'calendar':('Civil dates & time of day','Build calendar dates, weekdays and hour-minute-second fields.'),
 'zones':('Time-zone database','Resolve local times, UTC offsets, leap seconds and named time zones.'),
 'ctimes':('C-compatible time','Represent C-compatible broken-down time, time values and timestamps.'),
 'integer':('Integer widths & common types','Select integer width, pointer-sized values or standard size/difference types.'),
 'numericlimits':('Precision & numeric limits','Inspect numeric limits, floating-point environment and optional precision aliases.'),
 'arithmetic':('Complex, ratios & bit data','Represent complex arithmetic, compile-time ratios and fixed bit collections.'),
 'engines':('Random engines & seeds','Generate reproducible pseudo-random bits or obtain seed material.'),
 'distributions':('Random distributions','Transform engine output into a specified statistical distribution.'),
 'valarrays':('Numeric arrays & slices','Apply element-wise numeric array operations and indexed slices.'),
 'predicates':('Type-property predicates','Query type categories, construction, assignment, lifetime and relationships at compile time.'),
 'transforms':('Type transformations','Derive a type by changing qualifiers, references, pointers or other properties.'),
 'constants':('Constants & logical traits','Compose type-level constants and Boolean trait results.'),
 'sequences':('Compile-time index sequences','Generate integer/index packs for template expansion.'),
 'runtime':('Runtime type identity','Identify dynamic types and use type identity in associative lookup.'),
 'comparison':('Three-way comparison','Represent ordering categories and compute comparison result types.'),
 'wrappers':('Callable & reference wrappers','Store an invocable target or expose reference semantics.'),
 'operators':('Operator function objects','Use standard arithmetic, comparison, bitwise and logical callables.'),
 'searchers':('Search strategies','Package reusable subsequence-search strategies.'),
 'execution':('Execution policies','Select sequential, parallel or unsequenced standard algorithm execution.'),
 'exceptions':('Exceptions & failure types','Recognize exception categories and retain exception information.'),
 'errorcodes':('Error codes & conditions','Represent platform or portable failures without throwing by necessity.'),
 'tracing':('Source & stack diagnostics','Retain source locations and call-stack observations.'),
 'legacy':('Legacy facilities','Find deprecated C++23 interfaces when maintaining older code.'),
}

def classify(r):
    n=r['name']; q=r['qualifiedName']; hs=[r['primaryHeader']]; h=hs[0]
    has=lambda *names:any('<'+name+'>' in hs for name in names)
    if r['sourceFile']=='future.tex': return 'compatibility','legacy'
    if n=='formatter': return 'text','format'
    if n=='hash': return 'functions','operators'
    if n in ['tuple_size','tuple_element','tuple_element_t']: return 'values','products'
    if has('stdexcept','exception','system_error','stacktrace','source_location') or n.startswith('bad_'):
        if n in ('source_location','stacktrace_entry','basic_stacktrace','stacktrace'): return 'diagnostics','tracing'
        if has('system_error'): return 'diagnostics','errorcodes'
        return 'diagnostics','exceptions'
    if n=='hash': return 'functions','operators'
    if q.startswith('std::ranges::'):
        if n.endswith('_result'): return 'iteration','algorithmresults'
        if n.endswith('_view') or n in ['all_t','keys_view','values_view','adjacent_transform_view','pairwise_view','pairwise_transform_view']: return 'iteration','views'
        if has('functional'): return 'functions','operators'
        return 'iteration','rangetraits'
    if has('iterator'):
        if ('traits' in n or n.endswith('_tag') or n.startswith('iter_') or n=='projected'): return 'iteration','iteratortraits'
        return 'iteration','cursor'
    if n=='from_range_t': return 'containers','tags'
    if has('ranges'): return 'iteration','rangetraits'
    if has('generator','coroutine'): return 'concurrency','coroutines'
    if has('thread','stop_token'): return 'concurrency','threads'
    if n=='atomic' or has('atomic'): return 'concurrency','atomics'
    if has('future'): return 'concurrency','futures'
    if has('mutex','shared_mutex'): return 'concurrency','locks'
    if has('condition_variable','semaphore','latch','barrier'): return 'concurrency','coordination'
    if q.startswith('std::chrono::'):
        if n in ['treat_as_floating_point','duration_values','is_clock','is_clock_v']: return 'time','clocks'
        if n.endswith('clock') or n=='clock_time_conversion': return 'time','clocks'
        if n in ['duration','time_point','nanoseconds','microseconds','milliseconds','seconds','minutes','hours','days','weeks','months','years'] or n.startswith(('sys_','utc_','tai_','gps_','file_','local_')) and n not in ['local_info']: return 'time','durations'
        if n in ['tzdb','tzdb_list','nonexistent_local_time','ambiguous_local_time','sys_info','local_info','choose','time_zone','zoned_traits','zoned_time','zoned_seconds','leap_second','time_zone_link','leap_second_info']: return 'time','zones'
        return 'time','calendar'
    if has('ctime') or n=='tm': return 'time','ctimes'
    if has('memory_resource') or q.startswith('std::pmr::') and n in ['memory_resource','pool_options','polymorphic_allocator','synchronized_pool_resource','unsynchronized_pool_resource','monotonic_buffer_resource']: return 'memory','resources'
    if has('new'): return 'memory','allocation'
    if has('memory','scoped_allocator'):
        if n in ['unique_ptr','shared_ptr','weak_ptr','default_delete','owner_less','enable_shared_from_this','out_ptr_t','inout_ptr_t']: return 'memory','owners'
        return 'memory','allocators'
    if has('array','deque','forward_list','list','vector'): return 'containers','sequence'
    if has('map','set'): return 'containers','ordered'
    if has('unordered_map','unordered_set'): return 'containers','hashed'
    if has('queue','stack','flat_map','flat_set'):
        return 'containers','tags' if n.startswith('sorted_') else 'adapters'
    if has('span','mdspan'): return 'containers','arrayviews'
    if has('string','string_view'): return 'text','characters' if n=='char_traits' else 'strings'
    if has('regex'): return 'text','regex'
    if has('format'): return 'text','format'
    if has('charconv'): return 'text','numbertext'
    if has('cwchar','cuchar','cwctype') and n not in ['size_t','tm']: return 'text','characters'
    if q.startswith('std::filesystem::'): return 'io','filesystem'
    if has('locale','clocale'): return 'io','locales'
    if has('cstdio','cinttypes') and n in ['FILE','fpos_t']: return 'io','cfile'
    if r['sourceFile']=='iostreams.tex':
        if any(s in n for s in ['filebuf','ifstream','ofstream','fstream']): return 'io','filestreams'
        if any(s in n for s in ['stringbuf','istringstream','ostringstream','stringstream','spanbuf','spanstream']): return 'io','stringstreams'
        if 'syncbuf' in n or 'osyncstream' in n: return 'io','syncstreams'
        if n in ['basic_istream','basic_ostream','basic_iostream','istream','ostream','iostream','wistream','wostream','wiostream']: return 'io','streamio'
        if n=='imaxdiv_t': return 'numeric','arithmetic'
        return 'io','streambase'
    if has('random'): return 'numeric','distributions' if 'distribution' in n else 'engines'
    if has('valarray'): return 'numeric','valarrays'
    if has('complex','ratio','bitset','bit'): return 'numeric','arithmetic'
    if has('limits','climits','cfenv','stdfloat','cmath'): return 'numeric','numericlimits'
    if has('cstdint','cstddef'): return 'numeric','integer'
    if has('type_traits'):
        if n in ['integral_constant','bool_constant','true_type','false_type','conjunction','disjunction','negation']: return 'meta','constants'
        if n.startswith(('is_','has_','reference_constructs','reference_converts')) or n in ['alignment_of','rank','extent']: return 'meta','predicates'
        return 'meta','transforms'
    if has('typeinfo','typeindex'): return 'meta','runtime'
    if has('compare'): return 'meta','comparison'
    if has('functional'):
        if n in ['function','move_only_function','reference_wrapper','is_bind_expression','is_placeholder']: return 'functions','wrappers'
        if 'searcher' in n: return 'functions','searchers'
        return 'functions','operators'
    if has('execution'): return 'functions','execution'
    if n in ['integer_sequence','index_sequence','make_integer_sequence','make_index_sequence','index_sequence_for']: return 'meta','sequences'
    if has('tuple','utility'):
        if n.endswith('_t'): return 'values','construction'
        return 'values','products'
    if has('any','optional','variant','expected'): return 'values','choices'
    if has('initializer_list'): return 'containers','sequence'
    if n in ['va_list','jmp_buf','sig_atomic_t']: return 'numeric','integer'
    if n in ['div_t','ldiv_t','lldiv_t']: return 'numeric','arithmetic'
    raise ValueError('No category: '+q+' '+repr(hs))

PURPOSE = {
 'byte':'Distinct scoped enumeration representing an untyped byte of storage.',
 'size_t':'Unsigned integer type large enough to represent the size of an object.',
 'ptrdiff_t':'Signed integer type representing the difference between pointers into one array.',
 'max_align_t':'Trivial standard-layout type with alignment at least as strict as every scalar type.',
 'nullptr_t':'The type of the null pointer literal nullptr.',
 'endian':'Enumeration describing the byte order of scalar types.',
 'initializer_list':'Read-only view over a compiler-generated initializer-list array.',
 'array':'Fixed-size contiguous sequence; size is part of the type.',
 'vector':'Growable contiguous sequence with constant-time indexed access.',
 'deque':'Double-ended indexed sequence with efficient insertion at both ends.',
 'list':'Doubly linked sequence supporting stable nodes and splicing.',
 'forward_list':'Singly linked sequence with forward traversal.',
 'map':'Comparator-ordered unique keys mapped to values.',
 'multimap':'Comparator-ordered keys mapped to values, allowing repeated keys.',
 'set':'Comparator-ordered unique keys.', 'multiset':'Comparator-ordered keys, allowing repeated keys.',
 'unordered_map':'Hash-based unique keys mapped to values.',
 'unordered_multimap':'Hash-based keys mapped to values, allowing repeated keys.',
 'unordered_set':'Hash-based unique membership.',
 'unordered_multiset':'Hash-based membership allowing repeated equivalent keys.',
 'stack':'Last-in, first-out adapter over a sequence container.',
 'queue':'First-in, first-out adapter over a sequence container.',
 'priority_queue':'Adapter that exposes the highest-priority element first.',
 'flat_map':'Sorted key/value adapter backed by sequence containers.',
 'flat_multimap':'Sorted sequence-backed key/value adapter allowing repeated keys.',
 'flat_set':'Sorted sequence-backed unique-key adapter.',
 'flat_multiset':'Sorted sequence-backed key adapter allowing repeated keys.',
 'span':'Non-owning view over a contiguous sequence; preserve the underlying storage lifetime.',
 'mdspan':'Non-owning multidimensional array view with explicit mapping and access policy.',
 'extents':'Static and dynamic multidimensional extent information.',
 'dextents':'Alias for an extents type whose dimensions are all dynamic.',
 'layout_left':'Column-major contiguous multidimensional index mapping.',
 'layout_right':'Row-major contiguous multidimensional index mapping.',
 'layout_stride':'Multidimensional index mapping with explicitly supplied strides.',
 'default_accessor':'Default pointer-based access policy for mdspan.',
 'unique_ptr':'Move-only exclusive owner that releases its object through a deleter.',
 'shared_ptr':'Reference-counted shared ownership of an object or aliasing view.',
 'weak_ptr':'Non-owning observer of a shared-ownership control block.',
 'enable_shared_from_this':'Support obtaining shared or weak ownership from an already shared object.',
 'default_delete':'Default deleter for exclusive ownership of an object or array.',
 'out_ptr_t':'Adapt a smart owner to a pointer-to-pointer output API.',
 'inout_ptr_t':'Adapt a smart owner to a pointer-to-pointer input/output API.',
 'allocator':'Default typed allocation interface for standard containers.',
 'allocator_traits':'Uniform interface for allocator types and their associated operations.',
 'pointer_traits':'Uniform pointer metadata and pointer rebinding interface.',
 'scoped_allocator_adaptor':'Propagate allocators into nested allocator-aware objects.',
 'memory_resource':'Run-time polymorphic interface for raw memory allocation.',
 'polymorphic_allocator':'Typed allocator that delegates allocation to a memory_resource.',
 'monotonic_buffer_resource':'Fast allocation resource that releases all accumulated storage together.',
 'synchronized_pool_resource':'Thread-safe pooled allocation resource.',
 'unsynchronized_pool_resource':'Pooled allocation resource without internal synchronization.',
 'pool_options':'Configuration limits for pooled memory resources.',
 'allocation_result':'Pointer and actual allocation count returned by allocate_at_least.',
 'pair':'Product containing two potentially different value types.',
 'tuple':'Product containing an arbitrary compile-time list of value types.',
 'tuple_size':'Trait reporting the number of elements in a tuple-like type.',
 'tuple_element':'Trait selecting an element type in a tuple-like type.',
 'optional':'A value that may be disengaged; no separate dynamically allocated value is required.',
 'variant':'Type-safe alternative containing one of a fixed list of types.',
 'any':'Type-erased ownership of a copy-constructible value.',
 'expected':'A value on success or a distinct error value on failure.',
 'unexpected':'Explicit wrapper that constructs or assigns an expected error state.',
 'monostate':'Empty alternative useful as the first type in a variant.',
 'basic_string':'Owning, resizable character sequence parameterized by character type and allocator.',
 'basic_string_view':'Non-owning contiguous character view; it does not extend the source lifetime.',
 'char_traits':'Character operations used by strings, string views and streams.',
 'basic_regex':'Compiled regular-expression pattern parameterized by character type and traits.',
 'regex_traits':'Character and locale traits used by a regular-expression implementation.',
 'sub_match':'Iterator pair describing one matched or unmatched subexpression.',
 'match_results':'Sequence of captures and match metadata from a regular-expression operation.',
 'regex_iterator':'Forward iterator over successive matches in a character sequence.',
 'regex_token_iterator':'Forward iterator over selected captures or unmatched text.',
 'formatter':'Customization template defining how a type is formatted.',
 'range_formatter':'Formatting support for ranges of formattable elements.',
 'to_chars_result':'End pointer and error code from numeric-to-character conversion.',
 'from_chars_result':'End pointer and error code from character-to-numeric conversion.',
 'path':'Native filesystem path representation with portable path-component operations.',
 'directory_entry':'Directory item with a path and optionally cached file metadata.',
 'directory_iterator':'Input iterator over entries in one directory.',
 'recursive_directory_iterator':'Input iterator traversing nested directory entries.',
 'file_status':'File type and permissions metadata.',
 'space_info':'Capacity, free and available filesystem space.',
 'thread':'An explicitly joined or detached thread of execution.',
 'jthread':'Joining thread that requests cooperative cancellation before joining at destruction.',
 'stop_token':'Observe whether cooperative cancellation has been requested.',
 'stop_source':'Own and request a cooperative cancellation state.',
 'stop_callback':'Register a callback to run when a stop request occurs.',
 'atomic':'Object providing indivisible operations and memory-order synchronization.',
 'atomic_ref':'Atomic access to an existing suitably aligned object during the reference lifetime.',
 'atomic_flag':'Minimal lock-free atomic flag with test/set/clear and waiting operations.',
 'mutex':'Exclusive, non-recursive mutual exclusion.',
 'recursive_mutex':'Exclusive mutual exclusion that one thread may acquire recursively.',
 'timed_mutex':'Exclusive mutual exclusion with timed acquisition.',
 'recursive_timed_mutex':'Recursive exclusive mutual exclusion with timed acquisition.',
 'shared_mutex':'Multiple shared readers or one exclusive writer.',
 'shared_timed_mutex':'Shared/exclusive locking with timed acquisition.',
 'lock_guard':'Scope-bound ownership of an already acquired exclusive lock.',
 'unique_lock':'Movable exclusive lock ownership with deferred, timed and manual operations.',
 'shared_lock':'Movable shared lock ownership with deferred and timed operations.',
 'scoped_lock':'Scope-bound ownership of one or several mutexes with deadlock avoidance.',
 'once_flag':'State used by call_once to perform initialization once.',
 'condition_variable':'Wait and notify threads with a unique_lock on a mutex.',
 'condition_variable_any':'Wait and notify using a user-supplied lock type.',
 'counting_semaphore':'Bounded permit counter for acquire/release coordination.',
 'binary_semaphore':'Semaphore specialization supporting a binary permit count.',
 'latch':'One-shot countdown synchronization point.',
 'barrier':'Reusable phase synchronization with an optional completion step.',
 'promise':'Producer endpoint for a value or exception stored in shared future state.',
 'future':'Move-only consumer of a value or exception from shared asynchronous state.',
 'shared_future':'Copyable consumer allowing repeated observation of shared asynchronous state.',
 'packaged_task':'Callable wrapper that stores a result or exception for a future.',
 'coroutine_handle':'Non-owning handle used to resume or destroy a coroutine frame.',
 'coroutine_traits':'Select the promise type associated with a coroutine signature.',
 'suspend_always':'Awaitable that always suspends.', 'suspend_never':'Awaitable that never suspends.',
 'noop_coroutine_promise':'Promise type of a no-op coroutine handle.',
 'generator':'Coroutine-backed input range that yields elements lazily.',
 'duration':'A time interval represented by a tick count and compile-time tick period.',
 'time_point':'An instant represented as a duration from the epoch of a clock.',
 'system_clock':'System-wide real-time clock with Unix-time conversion support.',
 'steady_clock':'Monotonic clock suitable for measuring elapsed time.',
 'high_resolution_clock':'Clock alias with the shortest supported tick period.',
 'utc_clock':'UTC time scale that accounts for leap seconds.',
 'tai_clock':'International Atomic Time clock.', 'gps_clock':'GPS time-scale clock.',
 'file_clock':'Clock associated with filesystem timestamps.',
 'time_zone':'Named time zone and its transitions and offsets.',
 'zoned_time':'Pair an absolute instant with a time zone for civil-time conversion.',
 'tzdb':'One time-zone database version with zones, links and leap seconds.',
 'tzdb_list':'Manage the current and retained time-zone database versions.',
 'local_info':'Result describing unique, nonexistent or ambiguous local time.',
 'sys_info':'UTC offset, daylight-saving adjustment and abbreviation for an interval.',
 'hh_mm_ss':'Decompose a duration into hour, minute, second and fractional fields.',
 'complex':'Complex number with real and imaginary components.',
 'ratio':'Normalized compile-time rational number.',
 'bitset':'Fixed-size sequence of bits with bitwise and indexed operations.',
 'valarray':'Numeric array supporting element-wise expressions and indirect selection.',
 'random_device':'Implementation-provided source of random values; may be deterministic.',
 'seed_seq':'Distribute integer seed material into engine state.',
 'default_random_engine':'Implementation-selected random-engine alias; not portable for reproducible sequences.',
 'numeric_limits':'Implementation characteristics and representable limits of a numeric type.',
 'function':'Copyable, type-erased wrapper for an invocable target.',
 'move_only_function':'Move-only, type-erased invocable wrapper with qualified signatures.',
 'reference_wrapper':'Copyable wrapper that behaves as a reference to another object.',
 'identity':'Function object returning its argument with value category preserved.',
 'source_location':'Source file, line, column and function information captured at a call site.',
 'stacktrace_entry':'One captured stack-frame observation.',
 'basic_stacktrace':'Allocator-aware sequence of captured stack-frame observations.',
 'error_code':'Error value and category, normally describing a concrete failure.',
 'error_condition':'Portable condition value and category used for comparison across implementations.',
 'error_category':'Interface defining error messages and condition equivalence for a category.',
 'system_error':'Exception carrying an error_code and explanatory message.',
 'exception':'Base interface for standard exception objects.',
 'exception_ptr':'Nullable shared ownership of a captured exception.',
 'nested_exception':'Retain an active exception inside another exception object.',
 'type_info':'Runtime type information produced by typeid.',
 'type_index':'Copyable, comparable and hashable wrapper for type_info.',
 'partial_ordering':'Three-way result that also represents unordered operands.',
 'weak_ordering':'Three-way result allowing equivalence without substitutability.',
 'strong_ordering':'Three-way result with strong equality and ordering semantics.',
 'subrange':'Iterator/sentinel pair packaged as a range, optionally with a stored size.',
 'dangling':'Placeholder result preventing accidental use of an iterator into a destroyed range.',
 'view_base':'Marker base used to recognize range views.',
 'view_interface':'CRTP base supplying convenient operations for a derived view.',
 'range_adaptor_closure':'CRTP base enabling pipe composition for user-defined range adaptors.',
 'ref_view':'Range view borrowing an existing lvalue range.',
 'owning_view':'Range view owning its underlying range.',
 'filter_view':'Lazy view retaining elements satisfying a predicate.',
 'transform_view':'Lazy view applying a callable to each element.',
 'take_view':'View of at most the first requested number of elements.',
 'drop_view':'View omitting the first requested number of elements.',
 'take_while_view':'View of the initial elements satisfying a predicate.',
 'drop_while_view':'View omitting initial elements while a predicate holds.',
 'split_view':'Split a range around delimiter patterns into subranges.',
 'lazy_split_view':'Lazily split a range, including supported input-only ranges.',
 'join_view':'Flatten a range of ranges.',
 'join_with_view':'Flatten ranges while inserting a delimiter pattern.',
 'zip_view':'Traverse several ranges together as tuples of references.',
 'zip_transform_view':'Traverse ranges together and apply a callable to their elements.',
 'cartesian_product_view':'Lazy Cartesian product of the underlying ranges.',
 'chunk_view':'View non-overlapping chunks of a requested size.',
 'slide_view':'View overlapping fixed-size windows.',
 'chunk_by_view':'Group adjacent elements while a binary relation holds.',
 'stride_view':'Traverse every nth element of a range.',
 'enumerate_view':'Pair each element with its index.',
 'adjacent_view':'View tuples of adjacent elements.',
 'adjacent_transform_view':'Apply a callable to each group of adjacent elements.',
 'reverse_view':'Traverse a bidirectional range in reverse.',
 'as_const_view':'Expose elements through constant iterators.',
 'as_rvalue_view':'Expose elements as rvalue references where supported.',
 'common_view':'Adapt a range to use the same iterator and sentinel type.',
 'elements_view':'Project one tuple-like element from each item.',
 'empty_view':'Range containing no elements.', 'single_view':'Range owning one value.',
 'iota_view':'Range of incremented values with an optional bound.',
 'repeat_view':'Range repeating one value, with an optional bound.',
 'basic_istream_view':'Single-pass range extracting values from an input stream.',
}

def purpose(r,group):
    if r.get('purpose'): return r['purpose']
    n=r['name']
    if r['kind']=='alias' and r.get('aliasOf') and '__EXPOSITION__' not in r['aliasOf']:
        rhs=r['aliasOf']
        if len(rhs)>180: rhs=rhs[:177]+'...'
        return 'Alias for '+rhs+'.'
    if n in PURPOSE: return PURPOSE[n]
    if n.startswith(('is_','has_')):
        return 'Compile-time trait for '+n.replace('_',' ')+'. Inspect its value member or corresponding _v variable.'
    if n.endswith('_distribution'):
        return 'Random distribution: '+n.removesuffix('_distribution').replace('_',' ')+'; transforms a uniform random bit generator into samples.'
    if n.endswith('_engine'):
        return 'Pseudo-random engine or engine adapter using '+n.removesuffix('_engine').replace('_',' ')+'.'
    if n.endswith('_result'):
        return 'Structured '+n.removesuffix('_result').replace('_',' ')+' result; retains the output fields specified by the algorithm or operation.'
    if r['kind']=='enum':
        return 'Enumeration selecting '+n.replace('_',' ')+'. '+GROUPS[group][1]
    if n.endswith('_t') and group in ['construction','allocation','tags','threads']:
        return 'Distinct tag type for '+n.removesuffix('_t').replace('_',' ')+' overload selection.'
    if group=='transforms':
        return 'Type transformation: '+n.replace('_',' ')+'. Inspect the resulting type member or corresponding _t alias.'
    if group=='constants': return 'Type-level constant or logical composition: '+n.replace('_',' ')+'.'
    if group=='operators': return 'Function object performing '+n.replace('_',' ')+'.'
    if group=='exceptions': return 'Standard failure type: '+n.replace('_',' ')+'. Check its documented conditions before catching or throwing it.'
    if group=='locales': return 'Locale facet or support type for '+n.replace('_',' ')+'.'
    if group=='calendar': return 'Civil calendar component: '+n.replace('_',' ')+'. Compose and validate dates with chrono calendar operations.'
    if group in ['streambase','streamio','filestreams','stringstreams','syncstreams']:
        return 'Character stream facility: '+n.replace('basic_','character-parameterized ').replace('_',' ')+'. '+GROUPS[group][1]
    return GROUPS[group][1]+' Facility: '+n+'.'

def catalogue():
    categories={cid:{'id':cid,'title':v[0],'purpose':v[1],'children':[]} for cid,v in CATEGORIES.items()}
    groups={}
    section_headers={h['section']:'<'+h['header']+'>' for h in headers}
    for q,r in sorted(records.items()):
        # A common type may be re-declared by several C-compatible headers.
        # Classify its semantic owner, not every header that imports its name.
        first_source_header=section_headers.get(r['sourceSection'])
        r['primaryHeader']=(first_source_header if first_source_header and first_source_header!='<iosfwd>'
            else next((h for h in r['headers'] if h!='<iosfwd>'),r['headers'][0]))
        owners={
            'size_t':'cstddef','ptrdiff_t':'cstddef','max_align_t':'cstddef','nullptr_t':'cstddef','byte':'cstddef',
            'tm':'ctime','mbstate_t':'cwchar','wint_t':'cwchar','div_t':'cstdlib','ldiv_t':'cstdlib','lldiv_t':'cstdlib',
            'intmax_t':'cstdint','uintmax_t':'cstdint','char_traits':'string',
            'atomic':'atomic','formatter':'format','hash':'functional',
            'tuple':'tuple','tuple_size':'tuple','tuple_element':'tuple','tuple_element_t':'tuple',
        }
        if r['name'] in owners: r['primaryHeader']='<'+owners[r['name']]+'>'
        cat,group=classify(r)
        if (cat,group) not in groups:
            node={'id':cat+'/'+group,'title':GROUPS[group][0],'purpose':GROUPS[group][1],'typeIds':[]}
            groups[cat,group]=node; categories[cat]['children'].append(node)
        r['id']=q
        r['categoryPath']=[cat,cat+'/'+group]
        r['purpose']=purpose(r,group)
        r['baseline']='C++23 (N4950)'
        if r['sourceFile']=='future.tex': r['status']='C++23; deprecated'
        if '<stdfloat>' in r['headers'] or re.match(r'^(?:u?int(?:8|16|32|64|ptr)_t)$',r['name']):
            r['availability']='Optional where the implementation supports the corresponding exact-width representation.'
        if r['namespace']=='std::pmr':
            r['purpose']='Polymorphic-allocation form. '+r['purpose']
        groups[cat,group]['typeIds'].append(q)
    for cat in categories.values():
        cat['count']=sum(len(g['typeIds']) for g in cat['children'])
        for group in cat['children']: group['count']=len(group['typeIds'])
    files=[{'file':stem+'.tex','sha256':hashlib.sha256((SRC/(stem+'.tex')).read_bytes()).hexdigest(),'url':BASE+stem+'.tex'} for stem in FILES]
    return {
      'schemaVersion':1,'title':'C++ standard library','retrievedAt':datetime.datetime.now(datetime.timezone.utc).isoformat(),
      'scope':{
        'baseline':'C++23 working draft N4950, tagged 10 May 2023',
        'claim':'Namespace-scope named classes, class templates, enumerations and type aliases explicitly declared in the retained N4950 header synopses, plus selected public nested and deprecated support types.',
        'organization':'Practical task categories are an authored classification, not namespace nesting, inheritance or an assertion that alternatives are interchangeable.',
        'limitations':[
          'This is a C++23 standard-library type catalogue, not a catalogue of every third-party C++ library or the unbounded ecosystem.',
          'Fundamental language types, functions, function overloads, concepts, macros, variables and enumerator constants are outside the type-leaf inventory.',
          'Template specializations are merged under their primary named family. Distinct aliases remain independently searchable.',
          'Implementation-defined and exposition-only helper types are excluded. Arbitrary member aliases and per-container iterator types are not expanded into separate leaves.',
          'Selected nested public types are included where they provide a distinct user-facing role; the nested-member inventory is not exhaustive.',
          'Deprecated C++23 facilities are isolated and marked. No C++26-only facilities are presented as C++23.',
          'Availability of headers and C++23 features still depends on the compiler and standard-library implementation.'
        ]},
      'sources':[
        {'title':'WG21 N4950 C++23 working draft','url':'https://www.open-std.org/jtc1/sc22/wg21/docs/papers/2023/n4950.pdf'},
        {'title':'C++ draft source release n4950','url':'https://github.com/cplusplus/draft/releases/tag/n4950','commit':'4e4de1d'},
        {'title':'N4950 HTML rendering for convenient clause navigation','url':'https://timsong-cpp.github.io/cppwp/n4950/','role':'Rendering of the same primary draft; declaration evidence is retained from the official source repository.'}
      ],
      'provenance':{'extractor':'scripts/programming-trees/catalog-cpp.py','files':files,'headerSynopses':headers},
      'coverage':{'typeCount':len(records),'categoryCount':len(categories),'groupCount':len(groups),
                  'headerSynopsisCount':len(headers),'headersWithTypeDeclarations':len(set(h for r in records.values() for h in r['headers'])),
                  'kinds':{k:sum(r['kind']==k for r in records.values()) for k in sorted(set(r['kind'] for r in records.values()))},
                  'deprecatedTypes':sum('deprecated' in r['status'] for r in records.values()),'supplementalTypes':len(SUPPLEMENTS)},
      'categories':list(categories.values()),'types':list(records.values())}

if __name__=='__main__':
    write_json(OUT/'cpp-extracted.json',{'headers':headers,'records':list(records.values())})
    result=catalogue()
    write_json(OUT/'cpp-types.json',result)
    with (OUT/'Cpp-standard-library-types.csv').open('w',encoding='utf-8-sig',newline='') as stream:
        writer=csv.writer(stream)
        writer.writerow(['Qualified name','Type kind','Header','Purpose','Status','Category','Subcategory','Category path','Source URL','Clause URL'])
        category_titles={c['id']:c['title'] for c in result['categories']}
        group_titles={g['id']:g['title'] for c in result['categories'] for g in c['children']}
        for r in result['types']:
            writer.writerow([r['qualifiedName'],r['kind'],r['primaryHeader'],r['purpose'],r['status'],category_titles[r['categoryPath'][0]],group_titles[r['categoryPath'][1]],' > '.join(r['categoryPath']),r['sourceUrl'],r['referenceUrl']])
    print(json.dumps(result['coverage'],indent=2))
