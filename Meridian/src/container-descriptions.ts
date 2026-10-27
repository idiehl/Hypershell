import type { ContainerVariant } from './core/types.ts';

export const CONTAINER_DESCRIPTIONS: Record<ContainerVariant,string>={
  spatial:'Freeform. Arrange child branches radially by default, or keep a manually arranged composition.',
  contained:'Freeform. Children begin at 3 o’clock, then alternate below and above in expanding rings. Drag a child to preserve a manual position.',
  horizontal:'Ordered. Equal-sized child tiles touch in one row, addressed 1:1, 2:1, 3:1…',
  vertical:'Ordered. Equal-sized child tiles touch in one column, addressed 1:1, 1:2, 1:3…',
  grid:'Ordered. Equal-sized tiles fill a square grid by row or column. Ten objects use a 4:4 grid.',
  spreadsheet:'Ordered. Define the column and row counts and the exact pixel dimensions of equal-sized cells.',
  window:'Ordered. Browse a defined spreadsheet through a smaller scrolling window of cells.',
  gallery:'Display one child object at a time. Use the arrows to browse; manage objects and their order in Relations.',
};

export const CONTAINER_CHOICES: ReadonlyArray<{value:Exclude<ContainerVariant,'spatial'>;label:string;category:'Ordered'|'Unordered'}>=[
  {value:'contained',label:'Freeform',category:'Unordered'},
  {value:'horizontal',label:'Horizontal',category:'Ordered'},
  {value:'vertical',label:'Vertical',category:'Ordered'},
  {value:'grid',label:'Grid',category:'Ordered'},
  {value:'spreadsheet',label:'Spreadsheet',category:'Ordered'},
  {value:'window',label:'Window',category:'Ordered'},
  {value:'gallery',label:'Gallery',category:'Ordered'},
];
