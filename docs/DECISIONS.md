# Decisões e validação

## Produto

- Recolhido: somente dentro da altura da taskbar.
- Painel: janela separada, só quando aberto explicitamente.
- Sem fallback que cubra aplicativos.
- Seek só no painel. O compacto não ganha barra de tempo.

## Ambiente

- Windows 11 Pro x64, build 26200, escala 100%
- Monitor 1920×1080, work area 1920×1032
- Taskbar inferior visível: `0,1032–1920,1080` (1920×48)

## Ocupação: completa, incompleta, indisponível

A revisão 0.2.0 aceitava `Success=true` se existisse qualquer retângulo (muitas vezes só a bandeja) depois de a UI Automation falhar em silêncio. Isso inventava vão livre no lugar de Iniciar/aplicativos.

A 0.2.1 ainda aceitava como Complete o caso “UIA sem folhas úteis + notify + só Widgets”, porque `HasStructuralCoverage` usava `Any`. A 0.3.0 exige cobertura adequada:

| Estado | Quando | Compacto |
|---|---|---|
| Complete | Árvore UIA terminou sem falha estrutural, a área de notificação foi lida, **e** há agrupamento de Iniciar (`Start`/`Search`/`TaskView`) **mais** `Apps` | Pode posicionar |
| Incomplete | Raiz UIA falhou, filhos falharam no meio da árvore, geometria de uma folha falhou, notify ausente, ou a cobertura estrutural não prova Iniciar+aplicativos | Oculto; bandeja permanece |
| Unavailable | Sem `Shell_TrayWnd`, timeout (2 s) ou exceção não tratada | Oculto |

Widgets e Win32 genérico não cobrem a barra inteira. IDs de automação com `App` (depois dos cheques de Iniciar/Pesquisa/Widgets/TaskView/Notify) entram como `Apps`.

Falha de `AutomationId` é opcional. Não se registra o mesmo `status:erro` em loop.

A UI Automation corre em thread MTA do pool, sem janelas, como [a documentação da Microsoft](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/ui-automation-threading-issues) pede para clientes que também têm UI própria. Só POCOs (`TaskbarOccupancy`) voltam à UI. Eventos são agrupados (280 ms). Há no máximo uma leitura em voo; um pedido novo incrementa a geração e descarta o resultado antigo **completo/incompleto**. Timeout publica `Unavailable("occupancy-timeout")` mesmo com pedidos mais novos na fila, para a posição antiga não ficar visível para sempre. Não se cria outro worker no timeout e não se afirma que a chamada externa foi cancelada: ela segue até acabar. `OccupancyApplyGate` impede aplicar um Complete/Incomplete velho depois que ele chega na fila da thread visual; um Unavailable de timeout mais novo que o último aplicado pode entrar. `CapsuleHost` só atualiza “layout suportado” em Complete/Incomplete.

Uma Incomplete dispara **uma** releitura ~400 ms depois e outra quando um conflito de shell termina.

## Compacto: âncora e recuperação do título

`TryPreserve` usa o vão que ainda contém a âncora (`current.Left`):

1. Se o vão cabe Full → Full na mesma âncora (ou clamp). Mini vira Full (`recover-full`).
2. Se só Mini cabe → Mini na mesma âncora.
3. Se a âncora deixou de estar num vão → recalcula do zero.
4. Ocupação desconhecida → Hidden, sem overlay acima da barra.

Não há histerese extra além do padding do vão (8 DIP). O título volta quando o espaço volta; a posição não salta para o outro intervalo livre se o atual continuar válido.

## Painel e seek

`PanelDismissPolicy`: manter só se o foco está nas nossas HWNDs, ou se o ponteiro está sobre elas **e** o botão primário está baixo (clique a caminho do compacto). Ponteiro parado não segura o painel. Alt+Tab fecha. Esc fecha o painel **exceto** durante a prévia de seek, quando só cancela a intenção. Sem janela de 350 ms e sem hook global.

A barra do painel é um `Slider` com template próprio (trilha ~3,5 DIP, hit ~22 DIP). Regras fora da View (`SeekCoordinator` + `SeekMapping`):

- Três tempos: posição real/estimada, prévia do arraste, pedido pendente à espera de reconciliação.
- Um comando por gesto concluído. Movimentos intermediários não saem.
- Timeline recebida durante o arraste não puxa o indicador de volta.
- Troca de `SessionInstanceId` ou `TrackGeneration` cancela a intenção. AUMID sozinho não identifica a operação.
- Falha, exceção ou ~2,5 s sem confirmação revertem a prévia e mostram aviso discreto. Sem modal.
- Gestos rápidos: só o pedido mais recente fica outbound; resultados antigos são descartados.
- Seek não altera play/pause.
- `TryChangePlaybackPositionAsync` recebe ticks (`TimeSpan.Ticks`). Intervalo válido usa `StartTime`/`EndTime`/`MinSeekTime`/`MaxSeekTime` e `IsPlaybackPositionEnabled`.
- Interpolação local só com o painel aberto e timeline utilizável; respeita animações reduzidas do sistema.

## As falhas fechadas nesta rodada

1. **Timeout com leitura bloqueada e pedido na fila** — Unavailable é publicado; o worker único espera a chamada antiga e só então lê a geração pendente.
2. **Complete com notify + Widgets** — Incomplete (`structural-coverage-insufficient`) até haver Iniciar+Apps.
3. **Resultado velho na dispatcher** — `OccupancyApplyGate` recusa Complete/Incomplete cuja geração já não é a atual.
4. **Clique/arraste na barra** — prévia começa no ponteiro; o soltar usa a posição do ponteiro, não o valor antigo do Slider.

## O que foi compilado

1. **Build** — Release 0.3.0 em `artifacts/win-x64`.
2. **Testes** — 87 aprovados. Não usam o desktop. Cobrem ocupação (timeout com fila, apply gate, Widgets/Win32/Apps-only) e seek (mapeamento com início ≠ 0, limites, prévia, um commit, falha/demora, troca de faixa/sessão, resultado velho, Esc/captura, teclado agrupado, pausa).
3. **Desktop** — HWND / `WindowFromPoint` / log / SMTC. Uma captura não substitui fluidez.

## Spotify observado nesta máquina (06/09/2026)

- AUMID: `SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify`
- Faixa usada na validação: “Dá Whisky Pra Essa Maluca” / DJ Jeeh FDC
- `IsPlaybackPositionEnabled=True`
- `start=00:00:00` `end=00:02:25.846` `minSeek=0` `maxSeek=end` (seek em toda a duração apresentada; início em zero)
- Segunda sessão (Brave) também reportou `seek=True`; a preferência continuou no Spotify
- `--seek-selftest`: 40 s `ok=True`, restore `ok=True`, permaneceu pausado

Não se assume que outras sessões sejam iguais.

## Desktop (Release 0.3.0, 06/09/2026)

| Checagem | Fonte | Resultado |
|---|---|---|
| Compacto `441,1040–641,1072` 200×32 na faixa `0,1032–1920,1080` | HWND | Sim |
| 1 px acima do compacto fechado = janela do editor/Explorer; Iniciar/apps = Explorer; centro = TaskBarHook | `WindowFromPoint` | Sem invasão da área dos apps |
| Painel `381,824–701,1032` 320×208 acima da barra; compacto inalterado | HWND | Sim |
| Clique na barra (~22%) | log + SMTC | Pedido 29,3 s; posição 29,3 s; continuou pausado |
| Arraste para frente (soltar ~78%) | log + SMTC | **Um** pedido 1:53,99; SMTC 1:53,99 |
| Arraste para trás (soltar ~20%) | log + SMTC | **Um** pedido 29,03 s; SMTC 29,03 s |
| Clique direto enquanto tocava (~55%) | log + SMTC | Pedido 1:20,44; SMTC 1:21,50; sessão `Playing` |
| Seek pausado | SMTC | Posição muda; `Paused` permanece |
| Play/pause no painel | SMTC | `Paused` → `Playing` (posição avançou 39 s → 45 s em ~2 s) → `Paused` de novo |
| Teclado (Seta direita, passo 5 s) | SMTC | 1:51,15 → 1:56,15 |
| Esc durante o arraste | log | Sem pedido novo |
| `--seek-selftest` | SMTC | ida e volta; playback preservado |
| Tela cheia exclusiva (janela cobrindo 1920×1080, não F11 no HWND do player) | log | Compacto/painel `Fullscreen`; ao fechar, compacto voltou |
| F11 no mesmo HWND de um aplicativo do usuário | — | Não disparado neste ambiente (para não roubar o foco dos apps em uso). A regra exclusiva foi exercitada pela janela de cobertura |
| Indicador sem atraso perceptível / sem salto ao soltar | observação humana da fluidez | Não gravado. O mapeamento soltar→SMTC bateu com o ponto do ponteiro; perseguição atrasada do indicador não foi filmada |
| Áudio nos alto-falantes | ouvido | Não verificado por audição direta. Evidência: `Playing` + posição avançando + salto SMTC no clique durante a reprodução |
| Troca de faixa ao vivo no Spotify | desktop | Não executada (evitou pular a faixa do usuário). Coberto pelos testes e pela simulação |
| Restauração | SMTC | Após o teclado (+5 s), a posição voltou a 1:51,15 pausada na mesma faixa |

## 0.3.1 — salto no arraste e botões cortados

### 1. Salto indevido durante o seek (causa)

Na 0.3.0, dois escritores atualizavam a prévia no mesmo gesto: `Panel_OnPreviewMouseMove`/`ApplyPointerPreview` calculava a fração manualmente (`x / ActualWidth`, largura cheia, sem considerar o indicador) e gravava `Slider.Value`; o `Slider` nativo (`IsMoveToPointEnabled`) aplicava o delta do arraste do `Thumb` sobre o valor já movido. Resultado: o delta entrava duas vezes — num arraste rápido 50%→20% a prévia manual chegava a 20%, o nativo aplicava −30% de novo e ia a 0%; 20%→80% ia a 100%. O `CompletePointerSeek` relia o ponteiro no soltar e corrigia o valor final, por isso o destino batiu nos testes da 0.3.0 mesmo com o salto visível no meio do gesto.

### 1. Solução: dono único manual, nativo suprimido

- O `PreviewMouseLeftButtonDown` sobre a barra abre a prévia e marca `Handled`: o `Slider`/`Track`/`Thumb` nativo nunca vê o gesto — sem salto próprio, sem captura própria, sem delta duplo.
- Cada movimento mapeia o ponteiro pela trilha útil (`FractionFromTrack`: `(x − thumb/2) / (track − thumb)`, clamp), ciente do indicador de 12 DIP. Agarrar o indicador preserva o deslocamento do dedo (sem snap); clicar na trilha pula o centro para o ponteiro.
- Captura própria no `Slider` durante o gesto (soltar fora da barra continua valendo, limitado ao intervalo). Soltar confirma a prévia com uma última amostra no ponto de soltar (cobre flicks rápidos). Um comando por gesto.
- `Slider.Value` é espelhado (suprimido) só para renderizar; `ValueChanged` não-suprimido virou rede de segurança.
- Teclado continua próprio (setas/Home/End suprimidos do nativo, um comando por `KeyUp`).
- Esc cancela a prévia sem comando; sem prévia no soltar, limpa sem enviar.
- Dica de tempo posicionada pelo centro do `Thumb` na trilha (`PART_Track`), não mais pela largura cheia.
- `TASKBARHOOK_SEEK_TRACE=1`: registra por gesto `samples/min/max/path` para auditar a trilha do indicador.
- Tentativa descartada na rodada: dono único = Slider nativo. A validação ao vivo mostrou que o `Slider` pula para o clique na trilha mas não continua arrastando (trilhas com 2 amostras, commit no ponto do clique) — regredia pressionar-e-arrastar para clique-apenas.
- Achados de renderização: o template do `Track` estava sem os bindings `Minimum/Maximum/Value/Orientation` (contrato do `Slider`) — adicionados; e o `Track` não rearranja o indicador quando só o `Value` muda — `SyncSliderFromViewModel` agora invalida o arranjo, então o indicador acompanha o progresso programático também.

### 2. Botões cortados (causa)

Painel 208 DIP: linhas 72 (capa) + ~49 (título/artista) + `*` (controles) + ~37 (slider + linha de erro que reservava ~15 DIP mesmo vazia) + 24 (borda/margens) deixavam ~26 DIP para botões de 36–40. Eles transbordavam a linha e a área de 22 DIP do slider roubava os cliques na faixa sobreposta.

### 2. Solução

- Altura 208 → 248 DIP (`PanelWindow.PanelHeight` + XAML; `CapsuleHost.PlacePanel` usa a constante, então a âncora acima da barra acompanha).
- Linha de erro colapsa quando vazia (`Trigger` para `""` e `{x:Null}`); quando exibida, ainda cabe sem espremer os controles.
- Linha de controles com margem `0,8,0,8`; medição isolada: 81 DIP para os botões, slider 206×22 sem interseção.

### Acabamento

- Botões do painel usam `AnimatedIconButtonStyle` (fades de hover/pressão 120–150 ms em camadas sobrepostas, sem trocar `Background`); com animações reduzidas no Windows, o painel volta ao `IconButtonStyle` instantâneo. Compacto inalterado.
- Indicador mantém escala 1→1,67 em 150 ms (instantâneo sem animações). Nenhuma animação persegue `Value` durante o arraste.
- Estilos extraídos de `App.xaml` para `Themes/SharedResources.xaml` (mesmo visual; permite carregar só recursos nos testes).

### Testes (24 novos, 111 no total)

`SeekSliderInteractionTests` (13 fatos + teoria de mapeamento + render do indicador) e `PanelLayoutTests` (3) rodam num thread STA dedicado com `Application` puro + recursos compartilhados e conteúdo do painel medido no tamanho real de cliente. Exercem eventos reais: `ValueChanged`, `DragStarted`/`DragCompleted`, `PreviewKeyDown/Up`, `KeyDown` (Esc/Espaço), `PreviewMouseMove/Up` — mais mapeamento trilha útil/indicador, centro do indicador renderizado no ponteiro, linhas, botões e sobreposição, inclusive com erro visível e texto 125%.

Aprendizado registrado: instanciar `TaskBarHook.App` nos testes executa o `OnStartup` real ao bombear o dispatcher (mutex de instância, `Shutdown()` quando o app está aberto). Por isso o runner usa `Application` puro.

### Desktop (Release 0.3.1, 10/09/2026)

| Checagem | Fonte | Resultado |
|---|---|---|
| Painel `425,784–745,1032` 320×248 acima da barra; compacto inalterado | UIA | Sim |
| Botões 36×36 / 40×40 / 36×36 inteiros dentro do painel, 30 DIP de folga até o slider | UIA + captura `artifacts/seek-validation/panel-031.png` | Sim |
| Compacto no taskbar (centro = TaskBarHook; apps = Explorer) | `WindowFromPoint` | Sim |
| Sessão disponível | `--probe` | Só Brave (vídeo pausado 10:01 em 5:07, `seek=True`); Spotify fechado |
| Arraste rápido ida/volta, inversão sem soltar, clique distante, extremidades, Esc, teclado | gesto ao vivo + trilha `Seek gesture` + SMTC | **Validado 10/09 (Brave pausado 10:01):** trilhas 9/9/13 amostras monotônicas, sem excursão (0.711→0.237, 0.180→0.763, 0.500→0.820→0.289); clique 0.923, extremidades 0:00/10:01 e restore 5:06.97 exatos; Esc sem comando; seta +5s; um pedido por gesto; Paused preservado |

## 0.4.0 — composição do painel e transições

Slider preservado por exigência: nenhum caminho de prévia, captura, mapeamento, teclado ou commit foi tocado nesta rodada; todos os testes de seek continuam passando sem alteração.

### Composição

- Topo lado a lado: capa 56×56 (cantos 8) + título 15 semibold e artista 12 cinza, bloco de texto centralizado na vertical. Sem artista, o título sozinho centraliza — estado intencional, sem buraco.
- Controles 36/40/36 centralizados no espaço flexível; seek + tempos fixos na base; erro colapsa quando vazio como antes.
- Altura 248 → 192 DIP (56 a menos de vazio). `PlacePanel` usa a constante: âncora acima da taskbar mantida.
- Títulos longos truncam com elipse em largura fixa; sem duração, a barra fina informativa continua (sem indicador).

### Transições

- Abrir: `RenderTransform` +8→0 DIP com opacidade 0→1 em 180 ms (`CubicEaseOut`). A subida nunca passa do vão de 8 DIP: a borda inferior encosta no topo da taskbar no máximo, sem atravessar. O compacto não se move (só o conteúdo do painel anima).
- Fechar: opacidade →0 com +6 DIP em 130 ms (`CubicEaseIn`), depois `Hide()` — nenhuma superfície invisível segue interceptando cliques.
- Toggles rápidos: um único `Storyboard` interrompido/reiniciado a partir dos valores atuais (sem fila, sem flash) + `PanelTransitionGate` (geração/intenção) para um `Completed` obsoleto nunca esconder um painel reaberto.
- Troca de faixa: fade 1→0.3→1 com subida de 3 DIP em 230 ms só no `HeaderGrid` (capa+textos); controles e slider intocados. Primeira apresentação não anima.
- Botões: hover 100/150 ms, pressão 80/150 ms (resposta imediata mantida). Tudo desligado com animações reduzidas no Windows.
- Sem sombra real: janela opaca sem `AllowsTransparency` não a projeta; borda fina de 1 px mantida.

### Testes (7 novos, 118 no total)

- `PanelLayoutTests`: reescritos para a nova estrutura (cabeçalho lado a lado, artista ausente estável) + garantias antigas (botões, sem sobreposição, erro, texto 125%).
- `PanelTransitionGateTests` (4, sem janela): sequências show/hide/show rejeitam conclusões obsoletas.
- `TrackTransitionTests` (STA): storyboard mira o `HeaderGrid` (nunca a janela) e a primeira apresentação não anima.
- Mecânica temporal dos storyboards (rampa, reversão suave) validada visualmente, não por testes.

### Desktop (Release 0.4.0, 10/09/2026, simulação)

| Checagem | Fonte | Resultado |
|---|---|---|
| Estados: normal, pausado, deslocado, sem capa, sem duração, título longo, sem artista/capa, erro, vazio | capturas `artifacts/visual-040/sim-*.png` | Todos coerentes; botões inteiros; erro não espreme controles |
| Abertura (6 quadros ~66 ms) | brilho médio 10.1→24.9→38.3→40.6→40.6→40.6 | Rampa de fade+subida dentro de ~200 ms, depois estável |
| Fechamento (4 quadros) | quadros + UIA | Fade e sumiço; quadro final mostra o desktop atrás, sem fantasma |
| Troca de faixa (3 quadros ~114 ms) | brilho 41.2→39.0→41.2 | Mergulho no quadro do meio, controles/slider parados |
| Toggle rápido ×3 + clique fora no meio da transição | UIA | Estados finais corretos (aberto 320×192; fechado) |
| Slider 206×22 vs botões | UIA | 26 DIP de folga, sem interseção |
| Gravação em vídeo | — | Sem ffmpeg/OBS nesta máquina; sequências de quadros no lugar |

## 0.4.1 — deslocamento das transições parado

### Causa

Os storyboards miravam o transform diretamente (`SetTarget(transform)` + `new PropertyPath(TranslateTransform.YProperty)`). Em tempo de execução, com janela real e pump, o filho do fade (alvo Window) anexava o clock e animava, enquanto o filho do deslocamento nunca anexava nada: instância e caminho corretos, mas `HasAnimatedProperties=False` no meio do voo, Y travado em 8 e conteúdo renderizado deslocado (confirmado por `TransformToAncestor` e pela faixa no topo das capturas). `BeginAnimation` direto no mesmo DP funcionava — o problema era só a rota storyboard→alvo-Freezable. O fade mascarava tudo: os 118 testes só verificavam existência/alvo do storyboard e brilho, nunca movimento.

### Solução (pontual)

Alvo no elemento com caminho composto, a forma canônica: `RootBorder` + `RenderTransform.Y` (abrir/fechar) e `HeaderGrid` + `RenderTransform.Y` (troca de faixa); opacidades em caminhos string. Nenhuma posição final forçada: os valores chegam a zero porque os clocks agora correm. Layout, slider, tempos e curvas inalterados.

Geometria conferida: a janela não se move (topo UIA constante durante a transição) e o compacto idem; o vão de 8 DIP é até o compacto, então no primeiro instante o conteúdo toca a faixa de 8 px acima dele com opacidade ~0 — sem cobertura visível da barra.

### Testes (6 novos, 124 no total)

`PanelAnimationTests`: janela real fora da tela (nunca ativada, sem roubo de foco) com pump em tempo real. Cobrem valores intermediários e finais de deslocamento e opacidade, offset renderizado (não só a propriedade), inversão no meio da abertura e do fechamento sem salto, toggles rápidos com estado final correto, e mergulho do cabeçalho com retorno a zero (controles/slider em opacidade 1). Com animações reduzidas, cobrem os estados finais instantâneos. A regressão foi provada contra o alvo antigo (falha "never moved mid-flight").

### Desktop (Release 0.4.1, 10/09/2026, simulação)

| Checagem | Fonte | Resultado |
|---|---|---|
| Abertura (6 quadros) | brilho 20.6→37.6 + topo UIA 840 constante | Fade e subida; janela ancorada |
| Faixa residual no topo | borda da capa: y=21 (0.4.0) → y=13 (0.4.1) | Exatos 8 px para cima; repouso limpo |
| Troca de faixa (3 quadros) | brilho 38.0→35.7→38.0 | Mergulho e retorno; controles parados |
| Fechamento (4 quadros) | quadros + UIA | Fade e sumiço; sem fantasma |
| Movimento real (não só brilho) | testes automatizados acima | Deslocamento + offset renderizado medidos |

## 0.4.2 — compacto flutuando sobre vídeo em tela cheia

### Causa

Amostra ao vivo (Brave, YouTube fullscreen): janela `Chrome_WidgetWin_1` com rect 0,0-1920,1080 cobrindo o monitor, **mas** com estilo `WS_MAXIMIZE` (`IsZoomed=True`) e área de trabalho ainda reservando a taskbar (0,0-1920,1032). A regra vetava `isMaximized && taskbarVisibleInWorkArea`, então o fullscreen nunca era detectado e o compacto ficava visível em 463,1040-663,1072 sobre o vídeo.

### Solução (pontual)

`FullscreenPolicy.IsExclusiveFullscreen` = `CoversMonitor` apenas. Na configuração suportada (taskbar inferior, monitor primário), uma janela maximizada normal para na área de trabalho + borda invisível de ~8 px (amostrada: -8,-8-1928,1040), longe dos 1078 exigidos — ou seja, cobrir o monitor já discrimina sozinho, e maximizado continua exibindo o compacto. D3D exclusivo (`QUNS=3`) e resto do fluxo inalterados; teste antigo com premissa errada convertido em regressão do caso real (amostra quns=2, sem falso D3D).

### Desktop (Release 0.4.2, 10/09/2026)

| Checagem | Fonte | Resultado |
|---|---|---|
| Vídeo fullscreen no Brave | log + UIA | `Fullscreen detected (... Chrome_WidgetWin_1 0,0-1920,1080 zoomed=True)`, `Compact=False`, HWNDs próprias fora da árvore UIA |
| Saída do fullscreen | log + UIA | `Fullscreen ended`, compacto de volta ao slot |
| Janela maximizada (não fullscreen) | UIA | Compacto visível (sem regressão) |

## 0.4.3 — fullscreen oscilando e trazendo o compacto de volta

### Causa

A 0.4.2 passou a detectar, mas o log mostrou detect→end em 0,3–40 s durante vídeo contínuo: qualquer amostra falsa isolada (troca de foco, transição de janela, corrida de hook) encerrava na hora e o compacto reaparecia sobre o vídeo. A saída era imediata; só a entrada tinha debounce.

### Solução (pontual)

- Debounce simétrico na saída: exige ~700 ms contínuos sem fullscreen para encerrar (atraso máximo de retorno ~1,2 s). Delays injetáveis no construtor para teste.
- `Fullscreen ended (...)` agora registra a causa da amostra falsa (ex.: `maximized class=...`, `Shell_TrayWnd`) para forense futura.
- 6 testes `FullscreenDebounceTests` com ambiente falso roteirizado: imunidade a amostra única, flap rápido ×10 sem evento, saída após espera, sem STA e sem usuário.

### Desktop (Release 0.4.3, 10/09/2026, self-test)

Janela sintética estilo Chrome (0,0-1920,1080 + `WS_MAXIMIZE`, 6 s, sem mídia real): um único `detected`, zero oscilação no período, um `ended (maximized ...)` limpo ao fechar e compacto de volta. Maximizado normal segue exibindo o compacto.

## 0.4.4 — sumiço mais rápido ao entrar em fullscreen

### Causa

Timer de 600 ms + confirmação de 500 ms: até ~1,1 s para esconder. Usuário percebia 1–2 s.

### Solução (pontual)

Entrada: timer 600→250 ms, confirmação 500→150 ms (duas amostras; glitch isolado ainda rejeitado). Saída conservadora em 700 ms (anti-flap da 0.4.3, intocada). 130/130 testes (delays injetáveis; nenhum teste prende os defaults).

### Desktop (Release 0.4.4, 10/09/2026, self-test)

Fullscreen sintético 5 s: detectado em 319 ms, zero oscilação, saída em 786 ms com causa (`maximized class=...`), compacto de volta. Maximizado normal inalterado.

## 0.4.5 — volta mais rápida ao sair do fullscreen

### Causa

Usuário: na saída, o compacto demorava ~1 s para voltar (debounce de saída de 700 ms + fase do timer). Medido antes: saída em 786 ms.

### Solução (pontual)

Saída 700→300 ms (amostras falsas isoladas continuam vetadas pela próxima amostra verdadeira). Entrada segue 150 ms. Defaults viraram `DefaultEntryDelay`/`DefaultExitDelay` com teste travando as faixas (entrada ≤250 ms, saída 200–500 ms, entrada < saída). 131/131 testes.

### Desktop (Release 0.4.5, 10/09/2026, self-test)

Fullscreen sintético 4 s: detectado em 270 ms, encerrado em 471 ms, exatamente 1 detect + 1 end (sem oscilação), compacto de volta.

## 0.5.0 — fullscreen robusto + temas do Windows

### Fullscreen: visibilidade real em vez de área reservada

Amostra ao vivo (Brave, vídeo): foreground 0,0-1920,1080 com `WS_MAXIMIZE`, work area ainda 0,0-1920,1032. A regra antiga lia "taskbar visível" da reserva e vetava: nunca detectava.

- `IsTaskbarEffectivelyVisible`: tray existe + `IsWindowVisible` + não-oculto (DWMWA_CLOAKED) + NÃO coberto pelo foreground (contenção total ±2 px; a borda invisível de ~8 px da maximizada só belisca). Veto de maximizada MANTIDO, agora contra visibilidade de verdade.
- QUNS D3D continua como complemento, nunca prova única.
- Hide imediato no fullscreen (sem fade disputando com o vídeo); todo hide cancela seek (preview, popup de tempo, captura — antes a captura vazava até o mouse-up e comia um clique). Painel não reabre após fullscreen nem abre durante (guardas na VM).
- Debounce 150/300 + detalhe no `ended` mantidos. Transições mesmo-HWND chegam por `LOCATIONCHANGE`.

### Tema: paleta semântica viva

- 19 chaves (`TaskBarHook.*`), `DynamicResource` em tudo; `ThemeManager` substitui brushes (mutação quebra: congelados — crash pego em validação). Sem recriar janelas, sem flash, sem tocar foco/seek.
- Hook `WM_SETTINGCHANGE` (todas as seções: HC não usa `ImmersiveColorSet`) nas duas janelas; `Refresh` é no-op quando nada muda.
- Sistema vence no misto (taskbar segue o sistema; documentado). Accent do registro `DWM\AccentColor` (DWM API divergia: laranja vs azul do usuário). Foco usa accent com piso 1.5 (3.0 suprimia até o azul-padrão; texto segue ≥4.5). HC mapeia `SystemColors` e ignora o resto.
- `FocusVisualStyle` em adorner não resolve `DynamicResource` (anel sumiu!): indicador movido para dentro dos templates com trigger `IsKeyboardFocused`.
- Limitação documentada: taskbar tingida (`ColorPrevalence`) não é reproduzida; superfícies sólidas.

### Testes (147)

Política reescrita (4, inclui veto restaurado), debounce (6), VM sem-restore/sem-abrir (2), STA hide-durante-gesto (2), animações (6), tema puro (9: valores dark exatos, contraste, mistos, bateria de accents, HC, fallbacks, manager) + STA follow do foco + no-disturb no seek.

### Desktop (Release 0.5.0)

| Checagem | Fonte | Resultado |
|---|---|---|
| Transições mesmo-HWND + painel pré-aberto | sintético rotulado | Detect/fim limpos, painel some na hora, sem restore, compacto volta |
| Maximizada normal | UIA | Compacto visível (sem regressão) |
| Desativação (equiv. Alt+Tab) | UIA + log | Painel fecha |
| Matriz temas (claro/escuro/mistos/accent/HC) | capturas `visual-050/` + log | Todas aplicadas pelo hook; mistas seguem o sistema; restore byte-a-bit verificado; mídia intocada |
| Foco laranja, compacto HC | capturas ampliadas | Anel accent renderizado; compacto HC correto |
| F11/Alt+Tab reais no navegador | — | Pendente no uso cotidiano (geometria idêntica à validada; forense armada no log) |

## 0.6.0 — contraste do foco e fila flutuante

### Foco ≥ 3:1

O anel (1 px tracejado) aplicava o accent a 50% de opacidade com piso 1.5:1. No fundo realmente pintado — hover (~16% branco/preto) e pressionado (accent 10–22%) — o azul-padrão ficava abaixo de 3:1 mesmo opaco.

- A paleta semântica não foi reestilizada; só `FocusRing` muda.
- Primeiro sobe a opacidade do accent; se falhar, ajusta a luminância mantendo o matiz; só então mistura com a tinta do tema.
- O piso 3:1 vale contra superfície, hover instantâneo, hover animado, pressionado e hover+pressionado.
- Alto contraste continua em `SystemColors.Highlight`.
- O azul-padrão no escuro fica um azul mais claro (~`#0A95FF`), ainda o mesmo accent.

### Fila «Próximas»

Controlo discreto no cabeçalho. Hover (280 ms de intenção) abre uma janela irmã ao lado (ou acima se não houver vão). O vão de 8 DIP não tem hit-test invisível: a tolerância de 160 ms cobre a travessia. Clique e teclado também abrem; hover não rouba o foco. Esc fecha a lista e mantém o painel. Fechar o painel ou entrar em fullscreen fecha lista e tooltips na hora. ~5 faixas visíveis (capa 32 + nome, elipse + tooltip), scroll só da fila. Consulta: sem reordenar, remover ou tocar para tocar.

### Fonte da fila

SMTC não expõe upcoming. A integração isolada chama só `GET https://api.spotify.com/v1/me/player/queue` (não playlist, histórico ou recomendações). PKCE público, variável `TASKBARHOOK_SPOTIFY_CLIENT_ID`, redirect `http://127.0.0.1:47823/callback`, escopos `user-read-currently-playing` e `user-read-playback-state`. Tokens com DPAPI. A fila da conta é confrontada com título/artista do SMTC antes de ser apresentada como continuação. `--simulate` é a única fila inventada, marcada como simulação.

## Limitações restantes

- A taskbar do Windows 11 pode ficar acima de uma janela `TOPMOST`; recolocamos a nossa em eventos, sem loop.
- Uma caminhada UIA “vazia” ainda é conservadora se Iniciar+Apps não puderem ser comprovados.
- Timeout de 2 s não aborta a chamada ao Explorer; só deixa de esperar na UI.
- Sem Top Mode, volume, playlists, login ou inicialização automática.
- A fila autenticada exige `TASKBARHOOK_SPOTIFY_CLIENT_ID` e os escopos acima; sem isso a UI diz o que falta e não inventa faixas.
