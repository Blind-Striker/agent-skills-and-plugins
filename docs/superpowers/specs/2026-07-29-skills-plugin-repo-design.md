# Kişisel Multi-Harness Skill & Plugin Reposu — Tasarım

**Tarih:** 2026-07-29
**Durum:** Onaylandı (brainstorming oturumu sonucu)

## Amaç

External skill repolarını (superpowers, mattpocock/skills, dotnet-skills, aspire-skills, dotnet-agent-skills) tek bir kişisel repoda curate edip `deniz-*` prefix'li modüler pluginler olarak paketlemek. Hedef harness'lar: **Claude Code (birincil)** ve **OpenCode**. Repo GitHub'da kişisel marketplace olarak yaşar (`claude marketplace add` ile tüketilir).

## Temel Kararlar

| Karar | Seçim |
|---|---|
| Harness'lar | Claude Code + OpenCode |
| Curation derinliği | Subset seçimi + frontmatter/trigger ayarı + içerik düzenleme + kendi skill'ler + tip dönüşümü (skill→command/agent) |
| Upstream stratejisi | Submodule + manifest + overlay (Yaklaşım A); upstream takibi kontrollü merge ile |
| Dağıtım | GitHub'da kişisel marketplace |
| Tooling dili | TypeScript/Node |
| İlk modüller | deniz-process, deniz-dotnet-general, deniz-dotnet-aspire, deniz-dotnet-akka |

## Tasarım İlkesi: Over-Engineering Yok

`tools/` küçük kalır: framework yok, plugin sistemi yok, konfigürasyon dili icadı yok. Beş komut (`build`, `inventory`, `eject`, `sync`, `validate`), düz TypeScript, hedef toplam birkaç yüz satır. Bir iş elle yapmak script yazmaktan ucuzsa elle yapılır (YAGNI). Örn. OpenCode agent permission eşlemesi ilk sürümde yok — ihtiyaç doğunca eklenir.

## Repo Yapısı

```
agent-skills-and-plugings/            (GitHub'da kişisel marketplace; public/private kararı implementasyonda)
├── .claude-plugin/
│   └── marketplace.json              # 4 plugin'i listeler (build günceller)
├── external/                         # submodule'ler — SALT OKUNUR
│   ├── superpowers/
│   ├── mattpocock-skills/
│   ├── dotnet-skills/
│   ├── aspire-skills/
│   └── dotnet-agent-skills/
├── curation/                         # kullanıcı kararları (repo'nun kalbi)
│   ├── deniz-process.yaml
│   ├── deniz-dotnet-general.yaml
│   ├── deniz-dotnet-aspire.yaml
│   └── deniz-dotnet-akka.yaml
├── overlays/                         # gövdesi düzenlenen dosyaların TAM kopyaları
│   └── <plugin>/<skill>/SKILL.md
├── skills/                           # sıfırdan yazılan kendi skill'ler (kaynak)
│   └── <plugin>/<skill>/SKILL.md
├── plugins/                          # BUILD ÇIKTISI — commit'lenir, elle dokunulmaz
│   └── deniz-*/
│       ├── .claude-plugin/plugin.json
│       ├── skills/ commands/ agents/ hooks/
├── opencode/                         # BUILD ÇIKTISI — OpenCode formatı
├── tools/                            # TypeScript scriptleri
├── docs/
└── package.json
```

Kurallar:
- `external/` ve `plugins/` asla elle düzenlenmez.
- Kullanıcı dünyası: `curation/`, `overlays/`, `skills/`.
- Kendi yazılan skill'ler de aynı build'den geçer (OpenCode çıktısı bedavaya gelir).
- Build çıktıları commit'lenir ki marketplace clone edildiğinde doğrudan çalışsın.

## Curation Manifest Formatı

Plugin başına bir YAML (`curation/<plugin>.yaml`):

```yaml
plugin:
  name: deniz-process
  description: "Deniz'in curated süreç skill'leri"
  version: 0.1.0

items:
  - source: superpowers/skills/brainstorming
    # alan yoksa: olduğu gibi kopyalanır

  - source: superpowers/skills/using-superpowers
    exclude: true                     # bilinçli reddedilenler görünür kalır

  - source: superpowers/skills/systematic-debugging
    frontmatter:
      description: "..."             # trigger kontrolü: description override

  - source: mattpocock-skills/skills/tdd
    as: command                       # tip dönüşümü: skill → slash command
    name: deniz-tdd                   # yeniden adlandırma

  - source: superpowers/skills/writing-plans
    body: overlay                     # gövde overlays/<plugin>/writing-plans/ 'tan

hooks:
  include: []                         # upstream hook'lar default DIŞARIDA
```

Superpowers'ın her-konuşmada-otomatik-devreye-girme davranışı bir SessionStart hook'udur; `deniz-process` bu hook'u paketlemeyerek davranışı kökten kapatır. Skill'lerin tetiklenmesi ise tek tek `description` override ile kontrol edilir.

## Build Pipeline (`npm run build`)

1. Manifest'leri oku.
2. Her item için: submodule'den dosyaları al → overlay varsa gövdeyi değiştir → frontmatter override'larını uygula → tip dönüşümünü uygula.
3. Kendi skill'leri (`skills/`) ekle.
4. **Referans yeniden yazma:** manifest'ten türetilen eşleme tablosuyla (`superpowers:brainstorming → deniz-process:brainstorming` gibi) tüm çıktı dosyalarında düz string replace. Ekstra config yok.
5. `plugins/<name>/` ve `opencode/` çıktısını üret; `marketplace.json`'ı güncelle.
6. Değişen dosyaları raporla.

## Diğer Komutlar

- **`npm run inventory`**: Tüm submodule'leri tarar; her skill/command/agent için ad, açıklama, tip, boyut, otomatik-tetiklenme davranışını `docs/inventory.md` katalog raporuna döker. **Kural: katalog çıkmadan skill-by-skill curation kararı verilmez.** Curation oturumları bu katalog üstünden modül modül yapılır.
- **`npm run eject <plugin> <skill>`**: Skill'in kopyasını `overlays/`'a çıkarır; sonrasında gövde orada düzenlenir.
- **`npm run sync`**: `git submodule update --remote` (tümü veya `--only <repo>`) → build → rapor: (a) manifest'te olmayan yeni skill'ler, (b) alınanlardan upstream'de değişenler (overlay'sizler otomatik güncellenir), (c) overlay'lilerde upstream değişimi → diff gösterilir, karar kullanıcının. Hiçbir şey otomatik commit edilmez.
- **`npm run validate`** (build'in parçası + CI'da GitHub Action):
  - Frontmatter şeması (name/description zorunlu, geçerli YAML)
  - Manifest'teki her `source` submodule'de mevcut mu
  - İsim çakışması (iki plugin aynı adı üretmesin)
  - **Sarkan referans:** çıktıda kalan upstream namespace'leri (`superpowers:` vb.) → uyarı; çözüm curation kararı (referans verileni de al, ya da eject edip referansı çıkar)
  - `marketplace.json` ↔ `plugins/` tutarlılığı
  - Windows uyumu (path uzunluğu/karakterler)

## Namespace ve Çakışma

- Claude Code skill'leri plugin adıyla namespace'ler: çıktılar `deniz-process:brainstorming` gibi olur; upstream ile teknik çakışma yoktur.
- Davranışsal kural: **bir deniz-\* plugin'i bir kaynağı kapsadığı an, upstream plugin/marketplace kaldırılır** (model iki benzer skill görüp bulanıklaşmasın).
- Repo içi ad çakışmalarını validate yakalar.

## Modüller ve Kaynak Eşleşmesi (kaba)

| Plugin | Ana kaynaklar | Profil |
|---|---|---|
| deniz-process | superpowers, mattpocock-skills | brainstorming, TDD, debugging, plan, code review, skill yazma. Hook yok; trigger'lar tek tek manifest'te. |
| deniz-dotnet-general | dotnet-skills, dotnet-agent-skills | C# standartları, EF Core, DI/config, serialization, test/diag skill + agent'ları |
| deniz-dotnet-aspire | aspire-skills, dotnet-skills aspire-* | Orchestration, integration testing, service defaults, monitoring |
| deniz-dotnet-akka | dotnet-skills akka-*, akka-net-specialist | Akka.NET pattern'leri, hosting, management, testing + agent |

Skill-by-skill listeler bu tasarımın kapsamı dışındadır; inventory katalogu üstünden ayrı curation oturumlarında modül modül doldurulur. İlk build'ler küçük manifest'lerle başlar.

## OpenCode Çıktısı

- Tüm OpenCode çıktısı repo'daki `opencode/` klasörüne üretilir; alt klasörler OpenCode konvansiyonlarını izler (`opencode/skill/`, `opencode/command/`, `opencode/agent/`). Tüketim tarafında bu klasörler OpenCode config'ine bağlanır (detay implementasyon planında).
- SKILL.md formatı açık standart (agentskills.io); OpenCode skill'leri native okur → skill'ler için dönüşüm ~passthrough.
- `as: command` işaretliler → OpenCode command markdown formatına.
- Agent'lar → OpenCode agent markdown formatına; permission eşlemesi ilk sürümde YOK (YAGNI).
- Claude-özgü, OpenCode'da karşılığı olmayan öğeler (hooks, allowed-tools vb.) düşürülür ve build raporunda listelenir — sessiz kayıp yok.
- İlham: wshobson/agents modeli ("harness-native artifacts, not lowest-common-denominator translations").

## Kapsam Dışı / Sonraya

- Skill-by-skill curation listeleri (ayrı oturumlar, inventory sonrası)
- OpenCode agent permission eşlemesi
- Codex/Cursor/Gemini çıktıları (ihtiyaç doğarsa wshobson modelinden uyarlanır)
- Otomatik upstream sync (cron/CI) — sync her zaman manuel tetiklenir
