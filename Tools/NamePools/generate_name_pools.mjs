import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const toolDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(toolDirectory, "..", "..");
const outputDirectory = path.join(repositoryRoot, "Models", "Soldiers", "Names");

const GIVEN_NAME_TARGET = 1100;
const SURNAME_TARGET = 2200;

// These are exact, case-insensitive exclusions. The list intentionally includes
// distinctive single-name characters and both halves of especially famous full
// names. Common real-world fragments are not rejected when they merely occur
// inside a longer original name.
const canonCollisionBlacklist = [
  "Abaddon", "Abnett", "Agatone", "Agemman", "Ahriman", "Alpharius",
  "Angron", "Aphael", "Areios", "Arvida", "Asmodai", "Asterion", "Astorath", "Azrael",
  "Baharroth", "Baldemort", "Balthus", "Belial", "Bile", "Bjorn", "Blackmane", "Boreas",
  "Cadian", "Caedis", "Cain", "Calgar", "Calistarius", "Cantos",
  "Cassius", "Cato", "Celestine", "Corax", "Cortez", "Creed", "Cawl",
  "Cypher", "Dante", "Darnath", "Dembski", "Diomedes", "Dorn", "Draigo", "Eidolon",
  "Eisenhorn", "Eldrad", "El'Jonson", "Emperor", "Erasmus", "Erebus", "Fabius",
  "Feirros", "Ferren", "Ferrus", "Garadon", "Garro", "Gaunt", "Ghazghkull",
  "Gideon", "Gilliman", "Gorgutz", "Grimaldus", "Grimnar", "Guilliman",
  "Helbrecht", "Hesperax", "Horus", "Huron", "Jaghatai", "Jain Zar",
  "Kantor", "Karlaen", "Kayvaan", "Kharn", "Kor'sarro", "Kryptman",
  "Leandros", "Lemartes", "Loken", "Logan", "Lorgar", "Lucius", "Lukas",
  "Lysander", "Magnus", "Malcador", "Marneus", "Mephiston", "Moloc", "Mortarion",
  "Njal", "Numitor", "Omegon", "Orikan", "Phaeron", "Perturabo", "Ragnar",
  "Ravenor", "Roboute", "Russ", "Sammael", "Sanguinius", "Sanguinor",
  "Seth", "Severus", "Sicarius", "Sigismund", "Shrike", "Stormcaller",
  "Stronos", "Talos", "Tarvitz", "Tigurius", "Titus", "Trajan",
  "Trazyn", "Tyberos", "Typhus", "Uriel", "Varro", "Ventris", "Vulkan", "Yarrick",
  "Yesugei", "Yvraine", "Zaephon", "Zahndrekh"
];

const forbidden = new Set(canonCollisionBlacklist.map(name => name.toLocaleLowerCase("en-US")));

const givenFamilies = [
  {
    stems: [
      "Acast", "Ader", "Aeg", "Aldren", "Alv", "Ambros", "Ancar", "Ansel",
      "Arct", "Arden", "Aster", "Aurel", "Bast", "Caed", "Cael", "Cair",
      "Cast", "Cyr", "Dac", "Daer", "Damar", "Decar", "Demer", "Drav",
      "Edr", "Elian", "Emer", "Evand", "Faust", "Fend", "Galen", "Hadren",
      "Helian", "Icar", "Iov", "Jovan", "Kaed", "Laert", "Leont", "Macc",
      "Marcen", "Nican", "Orest", "Phaed", "Quintar", "Rhem", "Sabin",
      "Tacen", "Ther", "Valen", "Xand", "Zeph"
    ],
    endings: ["an", "ar", "as", "en", "eo", "er", "ian", "ion", "or", "us"],
    variantsPerStem: 4
  },
  {
    stems: [
      "Adel", "Alber", "Alric", "Ansgar", "Arved", "Bald", "Beran", "Brand",
      "Conrad", "Dag", "Degen", "Eber", "Eck", "Emmer", "Erken", "Falk",
      "Fen", "Frid", "Gared", "Ger", "Gern", "Gisel", "Gott", "Had",
      "Hagen", "Hart", "Heid", "Hrod", "Ingar", "Jor", "Keld", "Konrad",
      "Leof", "Lud", "Odo", "Osric", "Raban", "Roder", "Siger", "Sten",
      "Sven", "Theod", "Torv", "Ulf", "Vig", "Volk", "Wald", "Wern",
      "Wolfram", "Zeger"
    ],
    endings: ["ard", "ek", "en", "er", "ic", "win", "rik", "und"],
    variantsPerStem: 4
  },
  {
    stems: [
      "Abran", "Adir", "Amon", "Azar", "Bara", "Caph", "Danel", "Elior",
      "Esran", "Gabr", "Hazar", "Ilyan", "Ishar", "Joram", "Kadar", "Kez",
      "Malk", "Matan", "Nad", "Nemer", "Oth", "Phael", "Qadir", "Rafan",
      "Raz", "Sabar", "Samel", "Tamar", "Uzz", "Yoram", "Zakar", "Zev"
    ],
    endings: ["ael", "an", "ar", "as", "iel", "im", "ion", "or"],
    variantsPerStem: 4
  },
  {
    stems: [
      "Alek", "Arkad", "Bohd", "Boran", "Bran", "Draz", "Feod", "Gavr",
      "Ilar", "Ily", "Jar", "Kalen", "Kaz", "Kir", "Marek", "Mikh",
      "Milor", "Nikol", "Olek", "Rad", "Rost", "Stav", "Vad", "Vas",
      "Yar", "Zarek", "Zor", "Zvezd"
    ],
    endings: ["an", "ek", "en", "imir", "in", "ir", "o", "os"],
    variantsPerStem: 4
  },
  {
    stems: [
      "Aed", "Ael", "Aer", "Brann", "Caer", "Cairn", "Dair", "Eir",
      "Fael", "Ferg", "Garr", "Iain", "Keir", "Lorc", "Mael", "Nial",
      "Odr", "Orin", "Rhon", "Taran", "Tor", "Varr"
    ],
    endings: ["ach", "an", "en", "ic", "oc", "on", "yn"],
    variantsPerStem: 4
  },
  {
    stems: [
      "Akir", "Arun", "Ashar", "Bahir", "Chand", "Dar", "Dev", "Harun",
      "Indar", "Jahan", "Kamal", "Kiran", "Nav", "Nir", "Pran", "Rahan",
      "Ravi", "Sahir", "Samir", "Taj", "Varun", "Zahir"
    ],
    endings: ["ad", "an", "ar", "esh", "id", "ir", "un"],
    variantsPerStem: 4
  },
  {
    stems: [
      "Akio", "Daich", "Gen", "Har", "Isen", "Jin", "Kaito", "Ken",
      "Hideo", "Mas", "Raiden", "Ren", "Sabur", "Shin", "Tad", "Takes",
      "Taro", "Yori"
    ],
    endings: ["an", "en", "iro", "o", "ori", "u"],
    variantsPerStem: 4
  },
  {
    stems: [
      "Amon", "Ankh", "Aten", "Baken", "Horem", "Iken", "Khaem", "Kheper",
      "Men", "Nekhet", "Paser", "Rakh", "Seneb", "Seten", "Tahar"
    ],
    endings: ["aten", "em", "en", "hot", "ir", "on", "or"],
    variantsPerStem: 4
  },
  {
    names: [
      "Abraxen", "Adrest", "Aevor", "Aldric", "Althain", "Alveron",
      "Ambric", "Amethon", "Anovar", "Aquinor", "Arquen", "Arveth",
      "Avarn", "Bareth", "Belisar", "Beryn", "Boreas", "Borric",
      "Brennus", "Cadren", "Caedor", "Caelen", "Caleph", "Carad",
      "Cerdan", "Ceryn", "Cevran", "Corren", "Cyran", "Cyvar",
      "Daecon", "Daevan", "Damaric", "Darian", "Daven", "Deren",
      "Dorran", "Drustan", "Edran", "Eldric", "Elovar", "Emeric",
      "Endric", "Ephren", "Eraven", "Eryx", "Faeron", "Faron",
      "Ferren", "Galenor", "Garran", "Gethin", "Gorven", "Hadrik",
      "Halvern", "Hectoran", "Helovar", "Herac", "Ilyric", "Istran",
      "Jarek", "Jorvan", "Kaelor", "Kalen", "Kestian", "Khoren",
      "Lathen", "Leoric", "Lethan", "Lorian", "Maeron", "Malric",
      "Marovan", "Mavren", "Naver", "Nethan", "Norric", "Odran",
      "Orren", "Pellen", "Phaeron", "Quillon", "Radan", "Raevor",
      "Rethan", "Rhydan", "Saelen", "Saren", "Sorren", "Tavian",
      "Tavor", "Theren", "Tyren", "Ulric", "Vaelor", "Varen",
      "Veyran", "Wystan", "Xavian", "Yorven", "Zaephon", "Zorren",
      "Acalon", "Ademar", "Aethren", "Alcander", "Alveric", "Amarin",
      "Anderon", "Ardel", "Asteron", "Averic", "Balian", "Bastor",
      "Bereth", "Brannoc", "Caedmon", "Calven", "Carthen", "Cavor",
      "Corvin", "Cyneric", "Daeron", "Dastan", "Delmar", "Doran",
      "Eamon", "Edris", "Elric", "Evarn", "Fendrel", "Feran",
      "Gavren", "Godric", "Halen", "Havren", "Ivarn", "Jastor",
      "Kadran", "Keiran", "Lorcan", "Merek", "Neran", "Orric",
      "Padran", "Quintor", "Ravik", "Rorren", "Savar", "Teren",
      "Uthric", "Valric", "Wulfric", "Xandor", "Yevan", "Zarek",
      "Arioc", "Bardan", "Cairon", "Darven", "Eldan", "Falken",
      "Gavric", "Hadran", "Iskar", "Jorren", "Kestrel", "Lukan",
      "Mordan", "Nicanor", "Othren", "Perrin", "Ravian", "Stavor",
      "Tyran", "Vardek", "Werran", "Yoric", "Zavian", "Zevar"
    ]
  }
];

const surnameFamilies = [
  {
    first: [
      "Acar", "Aedr", "Afer", "Alber", "Albin", "Alcen", "Ambr", "Ancar",
      "Arven", "Aster", "Auren", "Balen", "Cadr", "Caelen", "Cassen",
      "Corven", "Cyren", "Damar", "Demer", "Eiren", "Evaren", "Faelen",
      "Galen", "Hadr", "Helven", "Iovar", "Juren", "Kaelen", "Lethen",
      "Maren", "Nican", "Orven", "Phaelen", "Quoren", "Raven", "Sabren",
      "Teren", "Valen", "Voren", "Xaren", "Zoren"
    ],
    second: [
      "ac", "ane", "ar", "aro", "ec", "en", "ian", "ien", "ik",
      "ion", "is", "or", "os", "ov", "us"
    ]
  },
  {
    first: [
      "Adel", "Amsel", "Bauer", "Beren", "Degen", "Eber", "Falk",
      "Gern", "Haber", "Hart", "Hein", "Heller", "Keller", "Kern",
      "Lenz", "Mauer", "Roth", "Schar", "Vogel", "Wal", "Weis",
      "Wern", "Wolf", "Zorn"
    ],
    second: [
      "ach", "auer", "beck", "el", "en", "er", "ert", "ich",
      "ig", "itz", "ler", "ner"
    ]
  },
  {
    first: [
      "Arkad", "Bel", "Bor", "Bran", "Cher", "Draz", "Gor", "Ily",
      "Jar", "Kaz", "Kov", "Malen", "Mark", "Miro", "Nov", "Rad",
      "Rost", "Saran", "Stav", "Vas", "Vol", "Yar", "Zar", "Zor"
    ],
    second: [
      "an", "enko", "ev", "in", "ov", "ovic", "ovsky", "ski",
      "vich", "yen", "zin"
    ]
  },
  {
    first: [
      "Aban", "Adar", "Azar", "Barak", "Danel", "Elior", "Hadar",
      "Ishar", "Kadar", "Malk", "Nadir", "Othar", "Qadir", "Razan",
      "Sabar", "Tamar", "Yoram", "Zakar"
    ],
    second: [
      "ad", "ael", "an", "ar", "iel", "im", "ir", "on", "or"
    ]
  },
  {
    first: [
      "Ak", "Aki", "Arashi", "Dai", "Gen", "Hara", "Isen", "Jin",
      "Kage", "Kai", "Kuro", "Mori", "Naga", "Rai", "Ren", "Sada",
      "Shiro", "Taka", "Tora", "Yama"
    ],
    second: [
      "gawa", "hane", "hara", "kawa", "mori", "mura", "naga",
      "saki", "shima", "tani", "yori"
    ]
  },
  {
    first: [
      "Aler", "Ambr", "Aurel", "Bellar", "Calar", "Cantar", "Cass",
      "Demer", "Domar", "Elian", "Faver", "Floren", "Galler", "Ilar",
      "Jovar", "Lucer", "Marcell", "Naver", "Olivar", "Paler", "Quint",
      "Ravel", "Saler", "Tavian", "Valer", "Vesper"
    ],
    second: [
      "a", "ano", "ari", "aro", "esi", "etti", "ino", "oni",
      "ori", "ucci"
    ]
  },
  {
    first: [
      "Aber", "Aven", "Bren", "Cad", "Caer", "Carad", "Deren", "Eiran",
      "Faron", "Garan", "Gwyn", "Kellan", "Loran", "Madoc", "Nery",
      "Owan", "Perran", "Rhyd", "Talan", "Teren", "Veyr", "Wyn"
    ],
    second: [
      "ach", "an", "en", "eth", "ey", "ic", "oc", "yn"
    ]
  },
  {
    first: [
      "Akr", "Andron", "Cal", "Damar", "Deme", "Eiren", "Galen",
      "Ikar", "Kall", "Kyr", "Leont", "Makar", "Niko", "Petren",
      "Stavr", "Theron", "Vasil", "Xanth"
    ],
    second: [
      "akis", "as", "atos", "es", "ides", "ios", "is", "os", "ou"
    ]
  },
  {
    first: [
      "Alvar", "Amad", "Arment", "Belar", "Cantar", "Cord", "Delar",
      "Estav", "Feral", "Galv", "Ibar", "Lored", "Mendar", "Navar",
      "Ortel", "Pavar", "Quint", "Ramir", "Salav", "Taver", "Valer",
      "Zamor"
    ],
    second: [
      "ado", "al", "ano", "ares", "era", "es", "ez", "ia", "o", "os"
    ]
  },
  {
    first: [
      "Arash", "Bahram", "Dary", "Farid", "Horm", "Jahan", "Kamr",
      "Mehr", "Navid", "Parv", "Rost", "Sahr", "Tav", "Vahr", "Yazd",
      "Zar"
    ],
    second: [
      "adi", "an", "ani", "ar", "avi", "esh", "i", "ian", "vand"
    ]
  },
  {
    first: [
      "Amar", "Arav", "Ashar", "Devan", "Dhar", "Ishan", "Kaly",
      "Kiran", "Mahir", "Navar", "Pran", "Rajan", "Ravan", "Samir",
      "Taran", "Var", "Vasant", "Yash"
    ],
    second: [
      "al", "an", "ani", "ar", "esh", "i", "in", "kar", "ur"
    ]
  },
  {
    first: [
      "Arcel", "Auden", "Belar", "Cavel", "Durel", "Evran", "Farel",
      "Giral", "Haver", "Jurel", "Laver", "Marot", "Nevar", "Orrel",
      "Parel", "Quarel", "Ravel", "Savar", "Turel", "Varen", "Yver"
    ],
    second: [
      "ain", "ard", "eau", "el", "elle", "et", "ier", "on", "ot"
    ]
  }
];

function joinParts(first, second) {
  const left = first.toLocaleLowerCase("en-US");
  const right = second.toLocaleLowerCase("en-US");
  const maximumOverlap = Math.min(3, left.length, right.length);

  for (let overlap = maximumOverlap; overlap > 0; overlap -= 1) {
    if (left.endsWith(right.slice(0, overlap))) {
      return first + second.slice(overlap);
    }
  }

  if (/[aeiou]$/i.test(first) && /^[aeiou]/i.test(second)) {
    return first.slice(0, -1) + second;
  }

  return first + second;
}

function isAcceptable(name) {
  if (!/^[A-Z][A-Za-z'-]{2,23}$/.test(name)) {
    return false;
  }

  const lower = name.toLocaleLowerCase("en-US");
  if (forbidden.has(lower)) {
    return false;
  }

  return !/(?:aaa|eee|iii|ooo|uuu|jj|qq|ww|yy|aiai|eiei|([a-z]{2,4})\1)/i.test(name);
}

function buildFamily({ names, stems, endings, first, second, variantsPerStem }) {
  if (names !== undefined) {
    return names.filter(isAcceptable);
  }

  const left = stems ?? first;
  const right = endings ?? second;
  const results = [];

  for (let leftIndex = 0; leftIndex < left.length; leftIndex += 1) {
    const variantCount = Math.min(variantsPerStem ?? right.length, right.length);
    for (let offset = 0; offset < variantCount; offset += 1) {
      const rightIndex = (leftIndex + offset * 2) % right.length;
      const name = joinParts(left[leftIndex], right[rightIndex]);
      if (isAcceptable(name)) {
        results.push(name);
      }
    }
  }

  return results;
}

function interleave(families) {
  const results = [];
  const longest = Math.max(...families.map(family => family.length));

  for (let index = 0; index < longest; index += 1) {
    for (const family of families) {
      if (family[index] !== undefined) {
        results.push(family[index]);
      }
    }
  }

  return results;
}

function uniqueCaseInsensitive(names) {
  const seen = new Set();
  return names.filter(name => {
    const key = name.toLocaleLowerCase("en-US");
    if (seen.has(key)) {
      return false;
    }
    seen.add(key);
    return true;
  });
}

function selectPool(families, target, label, exclusions = new Set()) {
  const candidates = uniqueCaseInsensitive(interleave(families.map(buildFamily)))
    .filter(name => !exclusions.has(name.toLocaleLowerCase("en-US")));
  if (candidates.length < target) {
    throw new Error(`${label} produced only ${candidates.length} candidates; ${target} required.`);
  }
  return candidates.slice(0, target).sort((a, b) => a.localeCompare(b, "en-US"));
}

function writeLines(filename, values) {
  fs.writeFileSync(path.join(outputDirectory, filename), `${values.join("\n")}\n`, "utf8");
}

fs.mkdirSync(outputDirectory, { recursive: true });

const givenNames = selectPool(givenFamilies, GIVEN_NAME_TARGET, "Given-name generation");
const givenNameKeys = new Set(givenNames.map(name => name.toLocaleLowerCase("en-US")));
const surnames = selectPool(
  surnameFamilies,
  SURNAME_TARGET,
  "Surname generation",
  givenNameKeys
);

const surnameKeys = new Set(surnames.map(surname => surname.toLocaleLowerCase("en-US")));
const overlap = givenNames.filter(name => surnameKeys.has(name.toLocaleLowerCase("en-US")));
if (overlap.length > 0) {
  throw new Error(`Cross-pool overlap detected: ${overlap.join(", ")}`);
}

writeLines("given_names.txt", givenNames);
writeLines("surnames.txt", surnames);
writeLines("canon_collision_blacklist.txt", [...canonCollisionBlacklist].sort((a, b) => a.localeCompare(b, "en-US")));

console.log(`Wrote ${givenNames.length} given names.`);
console.log(`Wrote ${surnames.length} surnames.`);
console.log(`Wrote ${canonCollisionBlacklist.length} collision exclusions.`);
