
/* Certifications, credentials and agreement statistics are the figures a
   reviewer will check. Until the real ones are supplied they carry this
   marker, so nothing here can be mistaken for a claim.                */
function illus(what){
  return '<div class="illus"><b>Illustrative</b><span>'+what+'</span></div>';
}
/* ====================================================================
   SkillHorizon - Assessment Studio
   The author/SME surface. It exists to answer three eligibility
   criteria that the other four persona views only assert:
     ET-01  difficulty adjusts inside the session, not between sittings
     ET-02  free text is rubric-scored in production, with published
            agreement against human scorers
     ET-04  every domain has named SMEs with stated credentials
   Synthetic data throughout.
   ================================================================= */

var SCALE = ['Awareness','Working','Practitioner','Advanced','Authority'];

/* ---------------------------------------------------------- item bank
   Counts per anchor skill. "free" is the share of items that ask for a
   typed answer rather than a selection - those are the ones that need
   a rubric and a marking-agreement check.                            */
var BANK = [
  // Cloud
  {ref:'C-01',name:'AWS / Azure solution architecture',pop:'Medium',items:62,free:21,diff:[4,13,22,18,5],sme:'K. Mehta',state:'live'},
  {ref:'C-02',name:'Kubernetes &amp; containerisation',pop:'Low',items:38,free:18,diff:[4,9,14,9,2],sme:'K. Mehta',state:'live'},
  {ref:'C-03',name:'Infrastructure as Code (Terraform)',pop:'Low',items:41,free:17,diff:[4,10,15,10,2],sme:'K. Mehta',state:'live'},
  {ref:'C-04',name:'FinOps &amp; cloud cost optimisation',pop:'Nil',items:12,free:25,diff:[1,4,5,2,0],sme:'K. Mehta',state:'thin'},
  {ref:'C-05',name:'Cloud security &amp; IAM',pop:'Low',items:36,free:19,diff:[3,9,14,8,2],sme:'K. Mehta',state:'live'},
  {ref:'C-06',name:'Site reliability engineering',pop:'Medium',items:59,free:20,diff:[5,13,21,15,5],sme:'K. Mehta',state:'live'},
  // Engineering
  {ref:'E-01',name:'Python / Java / TypeScript',pop:'High',items:96,free:18,diff:[8,21,34,26,7],sme:'L. Iyer',state:'live'},
  {ref:'E-02',name:'API &amp; microservices design',pop:'High',items:88,free:19,diff:[7,19,31,24,7],sme:'L. Iyer',state:'live'},
  {ref:'E-03',name:'CI/CD and DevSecOps',pop:'High',items:84,free:16,diff:[7,18,30,22,7],sme:'L. Iyer',state:'live'},
  {ref:'E-04',name:'Event-driven architecture',pop:'Nil',items:9,free:22,diff:[1,3,4,1,0],sme:'L. Iyer',state:'thin'},
  {ref:'E-05',name:'Test automation &amp; quality engineering',pop:'High',items:79,free:15,diff:[8,18,28,20,5],sme:'L. Iyer',state:'live'},
  {ref:'E-06',name:'Legacy modernisation',pop:'Medium',items:64,free:23,diff:[5,14,23,17,5],sme:'L. Iyer',state:'live'},
  // Data
  {ref:'D-01',name:'Data architecture &amp; modelling',pop:'Medium',items:71,free:19,diff:[4,12,26,22,7],sme:'A. Rao',state:'live'},
  {ref:'D-02',name:'Databricks / Snowflake platforms',pop:'High',items:86,free:17,diff:[7,19,31,22,7],sme:'A. Rao',state:'live'},
  {ref:'D-03',name:'Streaming &amp; real-time pipelines',pop:'Low',items:34,free:21,diff:[3,8,13,8,2],sme:'A. Rao',state:'live'},
  {ref:'D-04',name:'Data governance &amp; quality',pop:'Medium',items:66,free:22,diff:[5,15,24,17,5],sme:'A. Rao',state:'live'},
  {ref:'D-05',name:'Analytics engineering (dbt/SQL)',pop:'High',items:84,free:22,diff:[3,14,31,27,9],sme:'A. Rao',state:'live'},
  {ref:'D-06',name:'Data visualisation &amp; storytelling',pop:'High',items:81,free:24,diff:[6,18,28,22,7],sme:'A. Rao',state:'live'},
  // AI
  {ref:'A-01',name:'Machine learning engineering',pop:'High',items:77,free:21,diff:[6,17,27,21,6],sme:'A. Bhat',state:'live'},
  {ref:'A-02',name:'Generative AI &amp; LLM app design',pop:'High',items:74,free:27,diff:[5,16,26,20,7],sme:'A. Bhat',state:'live'},
  {ref:'A-03',name:'Prompt &amp; context engineering',pop:'Low',items:33,free:26,diff:[3,8,12,8,2],sme:'A. Bhat',state:'live'},
  {ref:'A-04',name:'RAG and vector data stores',pop:'Nil',items:7,free:29,diff:[1,2,3,1,0],sme:'A. Bhat',state:'thin'},
  {ref:'A-05',name:'MLOps &amp; model monitoring',pop:'Low',items:31,free:20,diff:[3,7,12,7,2],sme:'A. Bhat',state:'live'},
  {ref:'A-06',name:'Responsible AI and model risk',pop:'Low',items:31,free:34,diff:[2,7,11,9,2],sme:'A. Bhat',state:'review'},
  {ref:'A-07',name:'AI product &amp; use-case shaping',pop:'Nil',items:6,free:50,diff:[1,2,2,1,0],sme:'A. Bhat',state:'thin'}
];

/* Counts are derived, never typed. The previous version stated three
   different coverage figures on three pages; deriving them means the
   bank and the programme view cannot drift apart again.              */
function bankStats(){
  var t={items:0,free:0,withBank:0,thin:0,skills:BANK.length};
  for(var i=0;i<BANK.length;i++){
    var b=BANK[i];
    t.items+=b.items;
    t.free+=Math.round(b.items*b.free/100);
    if(b.state==='thin') t.thin++; else t.withBank++;
  }
  return t;
}

/* ------------------------------------------------------- the session
   One real 30-minute sitting, replayed item by item. This is ET-01:
   every row picks its difficulty from the estimate standing at that
   moment. Nothing here waits for a second sitting.                   */
var SESSION = {
  who:'Senior Associate, Data', skill:'D-05 Analytics engineering (dbt/SQL)',
  start:'Advanced (L4)', startWhy:'grade, talent group and current project - not a self-rating',
  items:[
    {q:'Pick the grain for a daily revenue mart',    kind:'select', d:3.6, ok:1, est:3.95, se:.72, next:'harder'},
    {q:'Incremental model fails on a late arrival',  kind:'select', d:4.2, ok:1, est:4.22, se:.58, next:'harder'},
    {q:'Rewrite a window function for a 40x table',  kind:'select', d:4.7, ok:0, est:3.88, se:.49, next:'easier'},
    {q:'Explain when a snapshot beats an increment', kind:'free',   d:4.0, ok:1, est:3.96, se:.42, next:'hold'},
    {q:'A partition rebuild changes a signed-off KPI',kind:'free',  d:4.3, ok:0, est:3.61, se:.37, next:'easier'},
    {q:'Choose the test that catches a silent drop', kind:'select', d:3.4, ok:1, est:3.52, se:.34, next:'hold'},
    {q:'Name who downstream you would tell, and when',kind:'free',  d:3.8, ok:0, est:3.38, se:.31, next:'easier'},
    {q:'Order four fixes by cost to the business',   kind:'select', d:3.2, ok:1, est:3.41, se:.29, next:'stop'}
  ],
  final:3.4, band:0.29,
  /* AE-12: the sitting was interrupted after item 5. What matters is not
     that it stopped - it is that item 6 was drawn from the estimate as it
     stood at the pause, not from a fresh start.                          */
  pause:{after:5, elapsed:'11m 40s', gapText:'2 days', resumed:'Thursday 9 October, 08:12',
         est:3.61, se:0.37, kept:['the running estimate and its band','the 5 items already served',
                                  'the exposure record, so none of them can reappear']}
};

/* --------------------------------------------------- marking quality
   ET-02. One free-text item, its rubric, what the machine gave, what
   the human panel gave, and the agreement figures behind shipping it. */
/* IB-05: the eight fields the RFP asks every item to carry. They sit on
   the item, not on the skill - a bank-level summary is not an item
   record, and a score challenge is answered from this.               */
var ITEM = {
  ref:'D-05-F14', skill:'D-05 Analytics engineering (dbt/SQL)', target:'L4 Advanced',
  type:'Free text, scenario', rubric:'RUB-D05-04 v2.1',
  author:'Ananya Rao', reviewer:'Sunita Varma',
  version:'v3 (stem reworded 14 Sep 2026)', status:'Live',
  history:[['v1','02 May 2026','Authored'],['v2','19 Jul 2026','Rubric split into four criteria'],
           ['v3','14 Sep 2026','Stem reworded after 6 candidates misread the timing']]
};

var MARK = {
  ref:'D-05-F14', stem:'A partition rebuild takes 40 minutes and will change a revenue figure that finance signed off yesterday. Say what you do, and in what order.',
  response:'I would not rebuild it in place during the day. First I\'d reproduce the difference on a copy so I can say exactly how big the change is and which dates move. Then I tell the finance owner before the number moves, not after - they signed it, so they have to be the one to re-sign it. If the delta is material I\'d hold the rebuild to the next close window and leave the old partition readable in the meantime.',
  criteria:[
    {n:'Sequencing',  w:30, m:4, h:4, d:'Acts before the number moves, not after'},
    {n:'Impact',      w:30, m:4, h:4, d:'Quantifies what changes and for whom'},
    {n:'Stakeholder', w:25, m:4, h:3, d:'Names the owner who must re-approve'},
    {n:'Risk',        w:15, m:3, h:3, d:'Protects the existing read path'}
  ],
  agree:[
    {ref:'D-05',n:'Analytics engineering',   nResp:412, exact:74, adj:97, qwk:.81},
    {ref:'D-01',n:'Data architecture',       nResp:388, exact:71, adj:96, qwk:.78},
    {ref:'E-01',n:'Python / Java / TS',      nResp:455, exact:76, adj:98, qwk:.83},
    {ref:'C-01',n:'Cloud architecture',      nResp:301, exact:69, adj:94, qwk:.74},
    {ref:'A-02',n:'GenAI &amp; LLM design',  nResp:188, exact:64, adj:91, qwk:.68},
    {ref:'A-06',n:'Responsible AI',          nResp:96,  exact:58, adj:88, qwk:.61}
  ]
};

/* ------------------------------------------------------- the SME panel
   ET-04. Named people, stated credentials, what each one owns, and when
   their sign-off was last refreshed.                                   */
var SMES = [
  {i:'AR',n:'Ananya Rao',    r:'Data',        c:'15 yrs data platform; ex-Head of Data Engineering, listed retail bank; MSc Statistics (IIT-B); dbt Certified',            own:'D-01, D-04, D-05, D-06', auth:213, rev:74, last:'18 Sep 2026'},
  {i:'LI',n:'Leena Iyer',    r:'Engineering', c:'18 yrs software engineering; Principal Engineer, payments; co-author of two internal engineering standards',               own:'E-01, E-03, E-04, E-05', auth:172, rev:91, last:'02 Oct 2026'},
  {i:'KM',n:'Karan Mehta',   r:'Cloud',       c:'12 yrs cloud infrastructure; AWS Solutions Architect Professional; Azure Solutions Architect Expert; CISSP',               own:'C-01, C-02, C-03, C-05', auth:134, rev:63, last:'27 Aug 2026'},
  {i:'AB',n:'Arjun Bhat',    r:'AI',          c:'9 yrs applied ML; PhD Machine Learning; published on LLM evaluation; chairs the internal model-risk forum',                own:'A-01, A-02, A-03, A-06', auth:88,  rev:52, last:'11 Oct 2026'},
  {i:'SV',n:'Sunita Varma',  r:'Cross-group', c:'Chartered psychologist (BPS); 20 yrs psychometrics; sets the rubric standard and signs off every marking-agreement study', own:'Rubric standard, all marking studies', auth:0, rev:188, last:'09 Oct 2026'}
];

/* Skills with no benchmarked population yet - they are also the thinnest
   part of the bank, and that is the honest thing to show. */
/* IB-08 asks for item content wherever coverage is Partial or None.
   The Weeks 2-4 authoring window covered every skill that had a
   population to calibrate against. These four had none, so a calibrated
   depth was not possible then - they are being authored now, ahead of
   wave 2, and nobody is measured on them until they are.
   Depth targets follow the IB-02 band minimums.                      */
var AUTHORING=[
 {ref:'C-04', need:48, have:12, sme:'Karan Mehta', rev:'Sunita Varma', phase:'drafting now', due:'14 Nov 2026', state:'drafting', done:19},
 {ref:'E-04', need:48, have: 9, sme:'Leena Iyer',  rev:'Sunita Varma', phase:'drafting now', due:'14 Nov 2026', state:'drafting', done:11},
 {ref:'A-04', need:48, have: 7, sme:'Arjun Bhat',  rev:'Sunita Varma', phase:'starts once C-04 lands', due:'21 Nov 2026', state:'scoping',  done:0},
 {ref:'A-07', need:48, have: 6, sme:'Arjun Bhat',  rev:'Sunita Varma', phase:'starts once E-04 lands', due:'28 Nov 2026', state:'scoping',  done:0}
];

var NILPOP = ['C-04','E-04','A-04','A-07'];

/* ================================================================ util */
function el(h){var d=document.createElement('div');d.innerHTML=h;return d.firstElementChild}
function esc(s){return String(s)}
function n(x){return x.toLocaleString('en-GB')}
function sum(a){var t=0;for(var i=0;i<a.length;i++)t+=a[i];return t}
function pct(v){return Math.round(v*100)}
/* ability 1..5 mapped to a 0..100 track position */
function px(v){return ((v-1)/4)*100}

/* ============================================================== pages */
var PAGES=[
 {id:'overview', icon:'◫', label:'Overview'},
 {id:'engine',   icon:'≡', label:'Adaptive engine', pill:'ET-01'},
 {id:'marking',  icon:'◇', label:'Rubric &amp; marking', pill:'ET-02'},
 {id:'panel',    icon:'✓', label:'SME panel', pill:'ET-04'},
 {id:'bank',     icon:'→', label:'Item bank', pill:'IB-01'},
 {id:'score',    icon:'▦', label:'Programme scorecard', pill:'DR-09'},
 {id:'ops',      icon:'◔', label:'Assessment operations', pill:'DR-04'},
 {id:'dva',      icon:'◎', label:'Declared vs assessed', pill:'DR-06'},
 {id:'pilot',    icon:'✔', label:'Pilot report', pill:'DR-03'},
 {id:'roll',     icon:'↻', label:'Rolling intake', pill:'PR-08'}
];

var PROG={invited:6485, sat:5412, target:85, nonresp:1073, median:27.4,
          plans:5412, baseline:21, skills:25, geos:5};

var OPS=[
 ['India',          3140, 2726, 26.9, 414],
 ['United Kingdom',  982,  838, 28.1, 144],
 ['United States',   874,  712, 27.6, 162],
 ['Poland',          901,  768, 27.2, 133],
 ['UAE',             588,  368, 28.8, 220]
];

var DVA=[
 ['D-05','Analytics engineering (dbt/SQL)', 1840, 4.1, 3.4, 23],
 ['E-01','Python / Java / TypeScript',      2410, 4.3, 3.9, 31],
 ['A-02','Generative AI &amp; LLM app design', 1392, 4.2, 3.1, 48],
 ['A-03','Prompt &amp; context engineering',   1655, 4.4, 3.0, 52],
 ['C-01','AWS / Azure solution architecture',1204, 4.0, 3.5, 27],
 ['A-06','Responsible AI and model risk',     418, 3.6, 2.6, 19],
 ['C-04','FinOps &amp; cloud cost optimisation', 96, 3.4, null, 11],
 ['E-04','Event-driven architecture',          142, 3.7, null, 14]
];

var PILOT={n:150, done:146, rate:97.3, median:27.8, over:4, credible:139, flagged:7,
 items:[
  ['D-05',22,'0.62','0.48','2m 10s','ok'],
  ['E-01',24,'0.58','0.51','1m 52s','ok'],
  ['C-01',19,'0.66','0.44','2m 26s','ok'],
  ['A-02',17,'0.71','0.29','3m 04s','weak'],
  ['A-06',12,'0.74','0.21','3m 31s','weak'],
  ['D-01',21,'0.60','0.46','2m 18s','ok']
 ]};

var ROLLING={
  since:'14 Sep 2026', sla:10, joined:214, assessed:178, planned:178, waiting:36, median:6.4,
  rows:[['Cloud',38,31,31,7],['Engineering',79,68,68,11],['Data',61,52,52,9],['AI',36,27,27,9]],
  steps:[
   ['Start date arrives','The HR feed carries joining date under IN-02. No one has to remember to add them.'],
   ['Anchor skills assigned','From role and role band, the same mapping the wave uses. No self-declaration.'],
   ['Invited within 2 working days','A 30-minute slot booked against their own calendar, not a wave window.'],
   ['Plan issued on completion','Same lift and shift logic. They are not waiting for the next wave to get one.']
  ]
};

function pScore(){
  var rate=(PROG.sat/PROG.invited*100);
  var short=PROG.target-rate;
  var h='';
  h+='<div class="head"><span class="eb">DR-09 &middot; programme scorecard</span>'+
     '<h1>Where wave 1 actually stands.</h1>'+
     '<p>One number per question the steering group asks. Completion is below target and is shown below target.</p></div>';
  h+='<div class="stats">'+
     '<div class="stat"><span>Invited</span><b>'+n(PROG.invited)+'</b><i>the full skill-tagged population</i></div>'+
     '<div class="stat"><span>Assessed</span><b>'+n(PROG.sat)+'</b><i>at least one anchor skill measured</i></div>'+
     '<div class="stat"><span>Completion</span><b>'+rate.toFixed(1)+'%</b><i>'+short.toFixed(1)+' points under the 85% target</i></div>'+
     '<div class="stat dark"><span>Anchor skills with a baseline</span><b>'+PROG.baseline+' / '+PROG.skills+'</b><i>the four with no population cannot have one yet</i></div>'+
     '</div>';
  h+='<div class="note"><span class="k">scope</span><div>The '+n(PROG.invited)+' above is the wave population &mdash; '+
     'everyone skill-tagged when the window opened. The '+n(ROLLING.joined)+' people who have joined since are '+
     '<b>not</b> counted in it; they run through rolling intake and are reported there. '+
     'Across both, '+n(PROG.sat+ROLLING.assessed)+' people have been measured.</div></div>';
  h+='<div class="note amb"><span class="k">below target</span><div>'+n(PROG.nonresp)+' people have not sat. At the current rate the wave closes at <b>'+rate.toFixed(1)+'%</b>, not 85%. UAE is the single biggest contributor and is the place to push.</div></div>';
  h+='<div class="card"><div class="card-h"><div><h2>The four questions this answers</h2>'+
     '<p>DR-09 names these four. Nothing else is on this page.</p></div></div><div class="card-b"><table><tbody>'+
     '<tr><td style="width:36%"><b>People assessed</b></td><td>'+n(PROG.sat)+' of '+n(PROG.invited)+' invited</td><td style="width:110px"><span class="chip amb">Under target</span></td></tr>'+
     '<tr><td><b>Completion rate</b></td><td>'+rate.toFixed(1)+'% against an 85% target</td><td><span class="chip amb">'+short.toFixed(1)+' pts short</span></td></tr>'+
     '<tr><td><b>Anchor skills with a published baseline</b></td><td>'+PROG.baseline+' of '+PROG.skills+'. C-04, E-04, A-04 and A-07 have no benchmarked population.</td><td><span class="chip gry">Expected</span></td></tr>'+
     '<tr><td><b>Individuals issued a plan</b></td><td>'+n(PROG.plans+ROLLING.planned)+
     ' &mdash; '+n(PROG.plans)+' in the wave and '+n(ROLLING.planned)+' through rolling intake, all issued automatically</td>'+
     '<td><span class="chip mint">100%</span></td></tr>'+
     '</tbody></table></div></div>';
  return h;
}

function pOps(){
  var h='';
  h+='<div class="head"><span class="eb">DR-04 &middot; live through the window</span>'+
     '<h1>Who has sat, who has not, and how long it took.</h1>'+
     '<p>Refreshed hourly while the window is open. Time-on-task is the median of completed sittings, against a 30-minute ceiling.</p></div>';
  var ti=0,ts=0,tn=0;
  for(var i=0;i<OPS.length;i++){ti+=OPS[i][1];ts+=OPS[i][2];tn+=OPS[i][4]}
  h+='<div class="stats">'+
     '<div class="stat"><span>Window</span><b>Open</b><i>closes Friday, week 10</i></div>'+
     '<div class="stat"><span>Median time on task</span><b>'+PROG.median+'m</b><i>against a 30-minute ceiling</i></div>'+
     '<div class="stat"><span>Non-responders</span><b>'+n(tn)+'</b><i>invited, not started</i></div>'+
     '<div class="stat dark"><span>Sittings today</span><b>412</b><i>peak 9am-11am India time</i></div>'+
     '</div>';
  h+='<div class="card"><div class="card-h"><div><h2>By geography</h2>'+
     '<p>TX-07 tags every result with geography, so the window can be worked where it is behind.</p></div>'+
     '<div class="right">hourly refresh</div></div><div class="card-b"><table>'+
     '<thead><tr><th>Geography</th><th>Invited</th><th>Sat</th><th>Completion against 85%</th><th>Median time</th><th>Not started</th></tr></thead><tbody>';
  for(var g=0;g<OPS.length;g++){
    var r=OPS[g], pcv=r[2]/r[1]*100, ok=pcv>=85;
    h+='<tr><td><b>'+r[0]+'</b></td><td>'+n(r[1])+'</td><td>'+n(r[2])+'</td>'+
       '<td><div style="display:flex;align-items:center;gap:9px">'+
       '<div class="meter" style="flex:1"><i style="width:'+pcv.toFixed(0)+'%;background:'+(ok?'var(--mint-strong)':'var(--amber)')+'"></i>'+
       '<span class="tgt" style="left:85%"></span></div>'+
       '<b style="font-size:11px;min-width:38px">'+pcv.toFixed(1)+'%</b></div></td>'+
       '<td>'+r[3]+'m</td><td>'+n(r[4])+(ok?'':' <span class="chip cor">push</span>')+'</td></tr>';
  }
  h+='</tbody></table></div></div>';
  h+='<div class="note"><span class="k">note</span><div>The red line on each bar is the 85% target. Only India and Poland are above it. UAE at '+(OPS[4][2]/OPS[4][1]*100).toFixed(1)+'% is the one that decides whether the wave lands.</div></div>';
  return h;
}

function pDva(){
  var h='';
  h+='<div class="head"><span class="eb">DR-06 &middot; declared against assessed</span>'+
     '<h1>What people said, next to what was measured.</h1>'+
     '<p>Declared values come through the alias map held under TX-04 &mdash; a person who wrote &ldquo;LangChain&rdquo; is counted against A-02. The gap column is the point of the page.</p></div>';
  h+='<div class="card"><div class="card-b"><table>'+
     '<thead><tr><th>Anchor skill</th><th>Declared by</th><th>Declared avg</th><th>Assessed avg</th><th>Gap</th><th>Aliases mapped</th></tr></thead><tbody>';
  for(var i=0;i<DVA.length;i++){
    var r=DVA[i], gap=(r[4]===null)?null:(r[3]-r[4]);
    var chip = gap===null?'gry':(gap>=1.0?'cor':(gap>=0.6?'amb':'mint'));
    var lab  = gap===null?'not measured':('+'+gap.toFixed(1)+' over');
    h+='<tr><td><span class="ref">'+r[0]+'</span><b>'+r[1]+'</b></td>'+
       '<td>'+n(r[2])+' people</td><td>'+r[3].toFixed(1)+'</td>'+
       '<td>'+(r[4]===null?'&mdash;':r[4].toFixed(1))+'</td>'+
       '<td><span class="chip '+chip+'">'+lab+'</span></td>'+
       '<td>'+r[5]+'</td></tr>';
  }
  h+='</tbody></table></div></div>';
  h+='<div class="note amb"><span class="k">read this</span><div>The two AI skills carry the widest gap &mdash; <b>prompt and context engineering sits 1.4 levels</b> below what people declared. That is the clearest argument in the whole programme for measuring rather than asking, and it is the number to put in front of the steering group.</div></div>';
  return h;
}

function pPilot(){
  var P=PILOT, h='';
  h+='<div class="head"><span class="eb">DR-03 &middot; the 150-person pilot</span>'+
     '<h1>What the pilot proved, and what it did not.</h1>'+
     '<p>The engine had to pass here before anyone at scale was asked to sit. Two skills did not pass and were held back.</p></div>';
  h+='<div class="stats">'+
     '<div class="stat"><span>Completed</span><b>'+P.done+' / '+P.n+'</b><i>'+P.rate+'% completion</i></div>'+
     '<div class="stat"><span>Median time</span><b>'+P.median+'m</b><i>'+P.over+' sittings hit the 30-minute ceiling</i></div>'+
     '<div class="stat"><span>Credible scores</span><b>'+P.credible+'</b><i>'+P.flagged+' flagged for review, not scored</i></div>'+
     '<div class="stat dark"><span>Items held back</span><b>2</b><i>weak discrimination, re-authored</i></div>'+
     '</div>';
  h+='<div class="card"><div class="card-h"><div><h2>Item statistics</h2>'+
     '<p>Difficulty is the share who got it right. Discrimination separates stronger from weaker candidates &mdash; below 0.30 an item is not doing its job.</p></div></div>'+
     '<div class="card-b"><table><thead><tr><th>Anchor skill</th><th>Items</th><th>Difficulty</th><th>Discrimination</th><th>Median time on item</th><th>Verdict</th></tr></thead><tbody>';
  for(var i=0;i<P.items.length;i++){
    var r=P.items[i], weak=r[5]==='weak';
    h+='<tr><td><span class="ref">'+r[0]+'</span></td><td>'+r[1]+'</td><td>'+r[2]+'</td>'+
       '<td><b>'+r[3]+'</b></td><td>'+r[4]+'</td>'+
       '<td><span class="chip '+(weak?'cor':'mint')+'">'+(weak?'Held back':'Passed')+'</span></td></tr>';
  }
  h+='</tbody></table></div></div>';
  h+='<div class="note amb"><span class="k">held</span><div><b>A-02 and A-06 did not pass.</b> Their items separate people poorly (0.29 and 0.21) and take the longest to answer. They were re-authored before wave 1 and the pilot results on those two skills were not used to place anyone.</div></div>';
  return h;
}

function pRoll(){
  var R=ROLLING, h='';
  h+='<div class="head"><span class="eb">PR-08 &middot; outside the wave</span>'+
     '<h1>People who join after the window opened.</h1>'+
     '<p>A wave has a start and an end. Hiring does not. Anyone who joins is picked up on their start date and run on the same engine against the same scale &mdash; they do not wait for the next wave to be measured or to get a plan.</p></div>';
  h+='<div class="stats">'+
     '<div class="stat"><span>Joined since '+R.since+'</span><b>'+n(R.joined)+'</b><i>after the wave window opened</i></div>'+
     '<div class="stat"><span>Assessed</span><b>'+n(R.assessed)+'</b><i>median '+R.median+' working days from start date</i></div>'+
     '<div class="stat"><span>Plans issued &middot; rolling</span><b>'+n(R.planned)+'</b><i>joiners only; the wave is counted separately</i></div>'+
     '<div class="stat dark"><span>Still inside the '+R.sla+'-day window</span><b>'+n(R.waiting)+'</b><i>invited, not yet sat</i></div>'+
     '</div>';
  h+='<div class="card"><div class="card-h"><div><h2>How a joiner is picked up</h2>'+
     '<p>No one schedules this by hand. The trigger is the start date on the HR feed.</p></div></div>'+
     '<div class="card-b"><table><tbody>';
  for(var i=0;i<R.steps.length;i++){
    h+='<tr><td style="width:34%"><b>'+(i+1)+'. '+R.steps[i][0]+'</b></td><td>'+R.steps[i][1]+'</td></tr>';
  }
  h+='</tbody></table></div></div>';
  h+='<div class="card"><div class="card-h"><div><h2>By talent group</h2>'+
     '<p>Waiting means inside the '+R.sla+'-working-day window, not overdue.</p></div></div>'+
     '<div class="card-b"><table><thead><tr><th>Talent group</th><th>Joined</th><th>Assessed</th><th>Plan issued</th><th>Progress</th><th>Waiting</th></tr></thead><tbody>';
  for(var g=0;g<R.rows.length;g++){
    var r=R.rows[g], pcv=Math.round(r[2]/r[1]*100);
    h+='<tr><td><b>'+r[0]+'</b></td><td>'+r[1]+'</td><td>'+r[2]+'</td><td>'+r[3]+'</td>'+
       '<td><div style="display:flex;align-items:center;gap:8px"><div class="meter" style="flex:1">'+
       '<i style="width:'+pcv+'%;background:var(--mint-strong)"></i></div>'+
       '<b style="font-size:11px;min-width:34px">'+pcv+'%</b></div></td>'+
       '<td>'+r[4]+'</td></tr>';
  }
  h+='</tbody></table></div></div>';
  h+='<div class="note"><span class="k">note</span><div>Rolling intake draws on the same item bank, so exposure control still holds across both routes: a joiner cannot be served an item the wave already showed them, and the 15% ceiling counts sittings from both.</div></div>';
  return h;
}

function pOverview(){
  var T=bankStats();
  var totItems=T.items, totFree=T.free;
  var thin=BANK.filter(function(b){return b.state==='thin'});
  var h='';
  h+='<div class="head"><span class="eb">Assessment Studio</span>'+
     '<h1>What the questions are, and who stands behind them.</h1>'+
     '<p>Three things decide whether this platform is allowed to measure anyone: the questions move with the answer inside one sitting, typed answers are marked against a published rubric and checked against human markers, and every domain has a named expert who signs the items. This is where all three are kept.</p></div>';

  h+='<div class="stats">'+
     '<div class="stat"><span>Items live</span><b>'+totItems+'</b><i>across '+T.withBank+' of the '+T.skills+' anchor skills</i></div>'+
     '<div class="stat"><span>Typed-answer items</span><b>'+totFree+'</b><i>each one rubric-marked, each rubric SME-signed</i></div>'+
     '<div class="stat"><span>SMEs on panel</span><b>'+SMES.length+'</b><i>credentials stated, sign-off dated</i></div>'+
     '<div class="stat dark"><span>Needs authoring</span><b>'+thin.length+'</b><i>thin banks &mdash; the same '+thin.length+' with no benchmarked population</i></div>'+
     '</div>';

  h+='<div class="note"><span class="k">ET-01</span><div>Difficulty moves <b>within</b> the sitting. The session below changes the next question after every answer and stops when the estimate is precise enough &mdash; it does not wait for a second sitting to get harder.</div></div>';

  h+='<div class="card"><div class="card-h"><div><h2>What is blocking a full run</h2>'+
     '<p>Honest state, not a status colour.</p></div><div class="right">4 items</div></div><div class="card-b"><table><tbody>';
  var blockers=[
    ['Four anchor skills have almost no items','C-04, E-04, A-04 and A-07 hold 34 items between them, and no benchmarked population to calibrate against. All four are now owned, with a reviewer, a depth target and a date before wave 2 opens - see the bank page.','amb','Plan set'],
    ['Responsible AI marking agreement is below the bar','A-06 sits at QWK 0.61 against a 0.70 ship rule. 96 scored responses is too thin to judge it either way.','amb','Hold'],
    ['Cloud panel sign-off is the oldest','K. Mehta last revalidated 27 Aug 2026. The standard is every six months.','amb','Watch'],
    ['Bank depth is uneven by design','Depth follows population band, as IB-02 requires: High skills carry 74 to 96 items, Low skills 31 to 41. A Low-population skill with 90 items would be wasted authoring.','gry','Working as intended']
  ];
  for(var b=0;b<blockers.length;b++){
    h+='<tr><td style="width:30%"><b>'+blockers[b][0]+'</b></td><td>'+blockers[b][1]+'</td>'+
       '<td style="width:88px"><span class="chip '+blockers[b][2]+'">'+blockers[b][3]+'</span></td></tr>';
  }
  h+='</tbody></table></div></div>';
  return h;
}

function pEngine(){
  var S=SESSION;
  var h='';
  h+='<div class="head"><span class="eb">ET-01 &middot; adaptive within the sitting</span>'+
     '<h1>The next question depends on the last answer.</h1>'+
     '<p>One sitting, replayed item by item. After every answer the estimate moves, the uncertainty narrows, and the engine picks the next question against the estimate as it stands at that moment. When the band is tight enough it stops early rather than filling time.</p></div>';

  h+='<div class="stats">'+
     '<div class="stat"><span>Opens at</span><b>L4</b><i>'+S.startWhy+'</i></div>'+
     '<div class="stat"><span>Items used</span><b>'+S.items.length+'</b><i>stopped early; the cap is 12</i></div>'+
     '<div class="stat"><span>Final estimate</span><b>'+S.final.toFixed(1)+'</b><i>Practitioner, just short of the L4 target</i></div>'+
     '<div class="stat dark"><span>Final band</span><b>&plusmn;'+S.band.toFixed(2)+'</b><i>stop rule is &plusmn;0.30 or 12 items, whichever first</i></div>'+
     '</div>';

  h+='<div class="card"><div class="card-h"><div><h2>'+S.skill+'</h2>'+
     '<p>'+S.who+' &middot; one 30-minute sitting &middot; the bar is the blue band, the line is the estimate</p></div>'+
     '<div class="right">8 items &middot; 3 typed</div></div><div class="card-b"><div class="ladder">';
  for(var i=0;i<S.items.length;i++){
    var it=S.items[i];
    var left=px(it.est-it.se), w=px(it.est+it.se)-px(it.est-it.se);
    if(S.pause && S.pause.after===i){
      h+='<div class="pauserow"><div class="pz">&#9208;</div><div>'+
         '<b>Paused here, '+S.pause.elapsed+' in &mdash; resumed '+S.pause.gapText+' later</b>'+
         '<span>'+S.pause.resumed+'. The session came back at an estimate of '+S.pause.est.toFixed(2)+
         ' &plusmn;'+S.pause.se.toFixed(2)+', which is exactly where it stopped. Item 6 was chosen from that, not from a fresh start.</span>'+
         '</div></div>';
    }
    h+='<div class="lrow '+(it.ok?'ok':'no')+'">'+
       '<div class="lno">'+(i+1)+'</div>'+
       '<div class="lq">'+it.q+'<span>'+(it.kind==='free'?'typed answer &middot; rubric-marked':'selected answer')+
         ' &middot; set at L'+it.d.toFixed(1)+' &middot; '+(it.ok?'credited':'not credited')+'</span></div>'+
       '<div><div class="track"><span class="band" style="left:'+left+'%;width:'+w+'%"></span>'+
         '<i style="left:0;width:'+px(it.est)+'%"></i></div></div>'+
       '<div class="lmeta"><b>'+it.est.toFixed(2)+'</b> &plusmn;'+it.se.toFixed(2)+'<br />next: '+it.next+'</div>'+
       '</div>';
  }
  h+='</div></div></div>';

  h+='<div class="grid2">';
  h+='<div class="card"><div class="card-h"><h2>How the next item is chosen</h2></div><div class="card-b"><table><tbody>'+
     '<tr><td><b>Starting point</b></td><td>Grade, talent group and current project set the opening difficulty. No self-rating is used.</td></tr>'+
     '<tr><td><b>Selection</b></td><td>The item whose difficulty sits closest to the running estimate, so each question tells us the most it can.</td></tr>'+
     '<tr><td><b>After a credit</b></td><td>Estimate rises, next item is drawn harder.</td></tr>'+
     '<tr><td><b>After a miss</b></td><td>Estimate falls, next item is drawn easier.</td></tr>'+
     '<tr><td><b>Stops when</b></td><td>The band reaches &plusmn;0.30, or 12 items are used. Item 8 hit the band, so items 9 to 12 were never asked.</td></tr>'+
     '<tr><td><b>Exposure</b></td><td>No item may be served to the same person twice inside 180 days, and no item exceeds 15% of sittings for its skill.</td></tr>'+
     '<tr><td><b>If it is interrupted</b></td><td>The sitting can be paused and picked up later. What is held is '+S.pause.kept.join(', ')+'. Nothing is re-asked and nothing is reset.</td></tr>'+
     '</tbody></table></div></div>';
  h+='<div class="card"><div class="card-h"><h2>What this is not</h2></div><div class="card-b"><table><tbody>'+
     '<tr><td><b>Not a fixed form</b></td><td>Two people sitting the same skill on the same day see different questions in a different order.</td></tr>'+
     '<tr><td><b>Not between sittings</b></td><td>The adjustment happens between question 3 and question 4, not between January and April.</td></tr>'+
     '<tr><td><b>Not a time filler</b></td><td>A confident result ends the skill early. The 30 minutes is a ceiling, not a target.</td></tr>'+
     '<tr><td><b>Not lost if interrupted</b></td><td>Thirty minutes in one sitting is the design. A person who has to stop resumes on the same path rather than starting again.</td></tr>'+
     '<tr><td><b>Not the whole record</b></td><td>This sitting moves one skill. The other five in the set are measured in their own right.</td></tr>'+
     '</tbody></table></div></div>';
  h+='</div>';
  return h;
}

function pMarking(){
  var M=MARK, h='';
  var mTot=0,hTot=0;
  for(var i=0;i<M.criteria.length;i++){mTot+=M.criteria[i].m*M.criteria[i].w/100;hTot+=M.criteria[i].h*M.criteria[i].w/100}

  h+='<div class="head"><span class="eb">ET-02 &middot; rubric-scored free text</span>'+
     '<h1>Typed answers are marked, and the marking is checked.</h1>'+
     '<p>Every typed answer is scored against a published rubric, criterion by criterion. A human panel scores a sample of the same answers, and the two are compared before the item is allowed near a real result. The figures below are the live ones, not a pilot.</p></div>';

  h+='<div class="card"><div class="card-h"><div><h2>One item, start to finish</h2>'+
     '<p><span class="ref">'+M.ref+'</span>typed answer &middot; D-05 Analytics engineering</p></div></div><div class="card-b">';
  h+='<div class="stem">'+M.stem+'</div>';
  h+='<p style="font-size:10px;letter-spacing:.09em;text-transform:uppercase;color:var(--muted);margin-bottom:7px">What the candidate wrote</p>';
  h+='<div class="resp">'+M.response+'</div>';
  h+='<p style="font-size:10px;letter-spacing:.09em;text-transform:uppercase;color:var(--muted);margin-bottom:9px">The rubric, and both scores</p>';
  h+='<div class="rub">';
  for(var c=0;c<M.criteria.length;c++){
    var cr=M.criteria[c], agreeC=cr.m===cr.h;
    h+='<div class="rc"><b>'+cr.n+' <span style="font-weight:400;color:var(--muted)">&middot; '+cr.w+'%</span></b>'+
       '<div class="lv">'+cr.d+'</div>'+
       '<div class="score"><span class="chip gry">machine</span><em>'+cr.m+'</em>'+
       '<span class="chip '+(agreeC?'mint':'amb')+'">human '+cr.h+'</span></div></div>';
  }
  h+='</div>';
  h+='<div class="note" style="margin:14px 0 0"><span class="k">result</span><div>Machine <b>'+mTot.toFixed(2)+'</b> against human <b>'+hTot.toFixed(2)+'</b> on a five-point scale. They split on <b>Stakeholder</b>: the machine credited naming the finance owner, the human marker wanted the re-approval step named too. One band apart, which counts as adjacent, not exact.</div></div>';
  h+='</div></div>';

  h+='<div class="card"><div class="card-h"><div><h2>The item&rsquo;s own record</h2>'+
     '<p>IB-05 &middot; every item carries these eight fields. A challenged score is answered from this row, not from the bank summary.</p></div></div>'+
     '<div class="card-b"><dl class="kv">'+
     '<dt>Anchor skill</dt><dd>'+ITEM.skill+'</dd>'+
     '<dt>Target level</dt><dd>'+ITEM.target+'</dd>'+
     '<dt>Item type</dt><dd>'+ITEM.type+'</dd>'+
     '<dt>Rubric reference</dt><dd><span class="ref">'+ITEM.rubric+'</span></dd>'+
     '<dt>Author</dt><dd>'+ITEM.author+'</dd>'+
     '<dt>Reviewer</dt><dd>'+ITEM.reviewer+'</dd>'+
     '<dt>Version</dt><dd>'+ITEM.version+'</dd>'+
     '<dt>Status</dt><dd><span class="chip mint">'+ITEM.status+'</span></dd>'+
     '</dl>'+
     '<p style="font-size:10px;letter-spacing:.09em;text-transform:uppercase;color:var(--muted);margin:15px 0 7px">Version history</p>'+
     '<table><tbody>'+ITEM.history.map(function(v){
       return '<tr><td style="width:60px"><span class="ref">'+v[0]+'</span></td><td style="width:110px">'+v[1]+'</td><td>'+v[2]+'</td></tr>';
     }).join('')+'</tbody></table></div></div>';

  h+=illus('Agreement statistics are placeholders. Replace with the figures from your own double-marking study.');
  h+='<div class="card"><div class="card-h"><div><h2>Agreement with human markers</h2>'+
     '<p>Double-marked samples, refreshed each quarter. The ship rule is a weighted agreement of 0.70 or better.</p></div>'+
     '<div class="right">last run 09 Oct 2026</div></div><div class="card-b"><table>'+
     '<thead><tr><th>Anchor skill</th><th>Responses double-marked</th><th>Exact</th><th>Within one band</th><th>Weighted agreement</th><th>State</th></tr></thead><tbody>';
  for(var a=0;a<M.agree.length;a++){
    var r=M.agree[a], pass=r.qwk>=.70, thin=r.nResp<150;
    h+='<tr><td><span class="ref">'+r.ref+'</span><b>'+r.n+'</b></td>'+
       '<td>'+r.nResp+(thin?'<span class="sub">thin sample</span>':'')+'</td>'+
       '<td>'+r.exact+'%</td><td>'+r.adj+'%</td>'+
       '<td><b>'+r.qwk.toFixed(2)+'</b></td>'+
       '<td><span class="chip '+(pass?'mint':'cor')+'">'+(pass?'Ships':'Held back')+'</span></td></tr>';
  }
  h+='</tbody></table></div></div>';

  var held=M.agree.filter(function(a){return a.qwk<0.70}).sort(function(a,b){return a.qwk-b.qwk});
  h+='<div class="note amb"><span class="k">held</span><div><b>'+held.length+' skills are below the bar and stay off.</b> '+
     held.map(function(a){return a.n+' ('+a.ref+') at '+a.qwk.toFixed(2)+' on '+a.nResp+' responses'}).join(', and ')+
     '. Typed answers on both are marked by a human until the figure clears 0.70 on a sample of at least 250. Nobody receives a machine-only result on either.</div></div>';
  return h;
}

function pPanel(){
  var h='';
  h+='<div class="head"><span class="eb">ET-04 &middot; named subject-matter experts</span>'+
     '<h1>Every domain has a person behind it.</h1>'+
     '<p>Items are authored and reviewed by named experts with stated credentials, not by a content team working from a syllabus. Sign-off is dated and expires after six months.</p></div>';

  h+=illus('People, credentials and sign-off dates are placeholders. Replace with the named SMEs you will actually put on the panel.');
  h+='<div class="card"><div class="card-h"><div><h2>The panel</h2>'+
     '<p>Authored means they wrote the item. Reviewed means they signed someone else’s.</p></div></div><div class="card-b">';
  for(var i=0;i<SMES.length;i++){
    var s=SMES[i];
    var old = s.last.indexOf('Aug')>-1;
    h+='<div class="sme"><div class="av">'+s.i+'</div>'+
       '<div class="sme-b"><b>'+s.n+'</b> <span class="chip gry">'+s.r+'</span>'+
       '<div class="cred">'+s.c+'</div>'+
       '<div class="cred" style="margin-top:5px">Owns '+s.own+'</div></div>'+
       '<div class="sme-r"><b>'+s.auth+'</b>authored<br /><b>'+s.rev+'</b>reviewed'+
       '<div style="margin-top:6px"><span class="chip '+(old?'amb':'mint')+'">signed '+s.last+'</span></div></div></div>';
  }
  h+='</div></div>';

  h+='<div class="card"><div class="card-h"><h2>Where the panel does not reach yet</h2></div><div class="card-b"><table>'+
     '<thead><tr><th>Anchor skill</th><th>Owner</th><th>Items</th><th>Why it is thin</th></tr></thead><tbody>';
  for(var b=0;b<BANK.length;b++){
    var k=BANK[b];
    if(k.state!=='thin') continue;
    h+='<tr><td><span class="ref">'+k.ref+'</span><b>'+k.name+'</b></td><td>'+k.sme+'</td>'+
       '<td>'+k.items+'</td><td>No benchmarked population in Annexure A, so there is no internal cohort to calibrate difficulty against. Items exist but are not yet trusted to place anyone.</td></tr>';
  }
  h+='</tbody></table></div></div>';
  return h;
}

function pBank(){
  var h='', T=bankStats(), thinItems=0;
  for(var q=0;q<BANK.length;q++) if(BANK[q].state==='thin') thinItems+=BANK[q].items;
  h+='<div class="head"><span class="eb">Item bank</span><h1>What exists, and how hard it is.</h1>'+
     '<p>Difficulty spread runs L1 to L5 left to right. A bank that is bunched in the middle cannot separate a Practitioner from an Authority, which is why the spread matters as much as the count.</p></div>';
  h+='<div class="card"><div class="card-b"><table>'+
     '<thead><tr><th>Anchor skill</th><th>Population</th><th>Items</th><th>Typed</th><th>Difficulty spread (L1 &rarr; L5)</th><th>SME</th><th>State</th></tr></thead><tbody>';
  for(var i=0;i<BANK.length;i++){
    var b=BANK[i], tot=sum(b.diff)||1;
    var bars='<div style="display:flex;gap:2px;align-items:flex-end;height:26px">';
    for(var d=0;d<b.diff.length;d++){
      var hh=Math.max(3,Math.round(b.diff[d]/tot*26));
      bars+='<span title="L'+(d+1)+': '+b.diff[d]+'" style="display:block;width:13px;height:'+hh+'px;border-radius:2px;background:'+(b.diff[d]===0?'rgba(23,34,31,.1)':'var(--forest-2)')+'"></span>';
    }
    bars+='</div>';
    var chip = b.state==='live'?'mint':(b.state==='review'?'amb':'cor');
    var lab  = b.state==='live'?'Live':(b.state==='review'?'In review':'Too thin');
    var nil  = NILPOP.indexOf(b.ref)>-1;
    h+='<tr><td><span class="ref">'+b.ref+'</span><b>'+b.name+'</b>'+(nil?'<span class="sub">no benchmarked population</span>':'')+'</td>'+
       '<td><span class="chip gry">'+b.pop+'</span></td><td><b>'+b.items+'</b></td><td>'+b.free+'%</td><td>'+bars+'</td><td>'+b.sme+'</td>'+
       '<td><span class="chip '+chip+'">'+lab+'</span></td></tr>';
  }
  h+='</tbody></table></div></div>';
  h+='<div class="card"><div class="card-h"><div><h2>Plan to close the thin banks</h2>'+
     '<p>IB-08 &middot; every skill with partial or no coverage has an owner, a reviewer, a depth target and a date. All four land before wave 2 opens.</p></div>'+
     '<div class="right">'+AUTHORING.length+' skills</div></div><div class="card-b"><table>'+
     '<thead><tr><th>Anchor skill</th><th>Now</th><th>Target depth</th><th>Progress</th><th>Author</th><th>Reviewer</th><th>Due</th></tr></thead><tbody>';
  for(var a=0;a<AUTHORING.length;a++){
    var p=AUTHORING[a], written=p.have+p.done, pcv=Math.min(100,Math.round(written/p.need*100));
    var row=null;
    for(var z=0;z<BANK.length;z++) if(BANK[z].ref===p.ref) row=BANK[z];
    h+='<tr><td><span class="ref">'+p.ref+'</span><b>'+(row?row.name:'')+'</b>'+
       '<span class="sub">'+p.phase+'</span></td>'+
       '<td>'+p.have+'</td><td>'+p.need+' <span class="sub">Nil-band minimum</span></td>'+
       '<td><div style="display:flex;align-items:center;gap:8px"><div class="meter" style="flex:1;min-width:60px">'+
       '<i style="width:'+pcv+'%;background:'+(pcv>=50?'var(--mint-strong)':'var(--amber)')+'"></i></div>'+
       '<b style="font-size:11px">'+written+'/'+p.need+'</b></div></td>'+
       '<td>'+p.sme+'</td><td>'+p.rev+'</td>'+
       '<td>'+p.due+'</td></tr>';
  }
  h+='</tbody></table></div></div>';

  h+='<div class="note"><span class="k">IB-01</span><div>All '+T.skills+' anchor skills are listed. '+T.withBank+' carry a bank deep enough to place someone; the other '+T.thin+' &mdash; C-04, E-04, A-04 and A-07 &mdash; hold '+thinItems+' items between them and are marked too thin, which is why no baseline is published for them anywhere.</div></div>';
  return h;
}

var RENDER={overview:pOverview,engine:pEngine,marking:pMarking,panel:pPanel,bank:pBank,score:pScore,ops:pOps,dva:pDva,pilot:pPilot,roll:pRoll};

/* ============================================================== shell */
function buildNav(root){
  var n=root.querySelector('#assessment-studio-nav'), h='';
  for(var i=0;i<PAGES.length;i++){
    var p=PAGES[i];
    h+='<button data-page="'+p.id+'"><i>'+p.icon+'</i>'+p.label+
       (p.pill?'<span class="pill">'+p.pill+'</span>':'')+'</button>';
  }
  n.innerHTML=h;
  n.addEventListener('click',function(e){
    var b=e.target.closest('button[data-page]');
    if(b) show(b.getAttribute('data-page'));
  });
}
function showAssessmentPage(root,id){
  if(!RENDER[id]) id='overview';
  var wrap=root.querySelector('#assessment-studio-wrap');
  if(wrap) wrap.innerHTML=RENDER[id]();
  if(typeof window.SkillHorizonOnAssessmentViewChange==='function'){
    window.SkillHorizonOnAssessmentViewChange(id);
  }
}
function show(id){
  var root=document.getElementById('assessment-studio-root');
  if(root) showAssessmentPage(root,id);
}
window.SkillHorizonMountAssessmentStudio=function(root,initialView){
  if(!root||root.dataset.studioMounted)return;
  root.dataset.studioMounted='1';
  root.classList.add('embed');
  showAssessmentPage(root,initialView||'overview');
};
window.SkillHorizonShowAssessmentView=function(viewId){
  var root=document.getElementById('assessment-studio-root');
  if(root) showAssessmentPage(root,viewId);
};

function miraItems(){
  var T=bankStats(), thin=BANK.filter(function(b){return b.state==='thin'}),
      review=BANK.filter(function(b){return b.state==='review'}),
      fail=MARK.agree.filter(function(a){return a.qwk<0.70}).sort(function(a,b){return a.qwk-b.qwk});
  var out=[];
  if(fail.length) out.push({t:fail[0].n+' is marking below the bar',
    d:'Weighted agreement '+fail[0].qwk.toFixed(2)+' on '+fail[0].nResp+' responses, against a 0.70 rule. Typed answers on it are human-marked until it clears on a sample of at least 250.',
    who:'Sunita Varma signs the study'});
  out.push({t:thin.length+' banks are too thin to place anyone',
    d:thin.map(function(b){return b.ref}).join(', ')+' hold '+thin.reduce(function(a,b){return a+b.items},0)+' items between them. All four are the skills with no benchmarked population, so there is nothing to calibrate against yet.',
    who:'Needs an authoring decision from the panel'});
  var oldest=SMES.slice().sort(function(a,b){return a.last.slice(-4)+a.last<b.last.slice(-4)+b.last?-1:1})[0];
  out.push({t:'Cloud sign-off is the oldest on the panel',
    d:'Karan Mehta last revalidated 27 Aug 2026. The standard is six months, so this one is closest to lapsing.',
    who:'Karan Mehta to revalidate'});
  if(review.length) out.push({t:review[0].ref+' is still in review',
    d:'It is the only bank not yet live. It is also the one failing the marking check, so the two are the same problem.',
    who:'Arjun Bhat owns it'});
  return out;
}

/* Mira drawer omitted in L&D embed */