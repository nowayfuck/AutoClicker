# AutoClicker

Autoclicker para Windows feito em **C# e Windows Forms**. Repete cliques do mouse, teclas ou uma sequência de pontos gravados na tela. Não há limite total de cliques: continua até você usar **Parar** ou o atalho configurado.

## Baixar e usar

1. Baixe [`release-5k/AutoClicker.exe`](release-5k/AutoClicker.exe) e abra no Windows 64 bits.
2. Escolha **Mouse**, **Teclado** ou **Pontos**.
3. Ajuste a velocidade entre **500 e 5.000 CPS** pelo slider ou digite o valor exato no campo numérico. Se necessário, ajuste o atraso inicial.
4. Clique em **Iniciar** ou use **F6**. Para interromper, clique em **Parar** ou use **F7**.

O executável é **autocontido e de arquivo único**. Para usá-lo, não é preciso instalar Python, .NET ou outras bibliotecas. As preferências e os pontos são salvos em `settings.json` na mesma pasta do executável.

## Mini tutorial: pontos de toque

1. Selecione o modo **Pontos** e clique em **Selecionar pontos de toque**.
2. Clique nos lugares desejados da tela, na ordem em que deverão ser repetidos. Cliques no painel do AutoClicker não entram na gravação. Botões esquerdo, direito e do meio são preservados.
3. Volte ao painel e clique em **Finalizar gravação**. Você também pode usar **Parar** ou o atalho de parada.
4. Confira a ordem em **Ver pontos**, ajuste o CPS e inicie. Ao chegar ao último ponto, a sequência recomeça e segue até você parar.

O botão **?** na janela abre esse tutorial. A velocidade é o **total de cliques por segundo**, distribuído pelos pontos na ordem gravada, e não um CPS separado para cada ponto. A gravação aceita até 10.000 pontos. Um novo início de gravação substitui a sequência anterior ao finalizar.

## Controles

| Opção | O que faz |
| --- | --- |
| Mouse | Clica com o botão escolhido na posição atual do cursor. |
| Teclado | Repete a tecla capturada ao clicar no campo e pressioná-la. Aceita as teclas virtuais que o Windows entrega ao aplicativo, incluindo teclas de mídia e função. |
| Pontos | Repete as posições gravadas, com seus botões e sua ordem originais. |
| Velocidade | Ajusta a meta de 500 a 5.000 ações por segundo, inclusive durante a execução. |
| Atraso inicial | Espera de 0 a 10 segundos antes da primeira ação. |
| Atalhos globais | Clique nos campos **Iniciar** e **Parar** para escolher a tecla desejada. O app verifica se o Windows aceita o atalho antes de salvá-lo. |

As teclas padrão são **F6** para iniciar e **F7** para parar. A tecla repetida deve ser diferente desses dois atalhos. Para usar uma tecla atualmente ocupada por um atalho, troque primeiro o atalho. Como os atalhos são globais, escolher uma letra comum pode interceptá-la enquanto o app estiver aberto. Algumas teclas reservadas pelo próprio Windows podem não ser entregues ao aplicativo.

## Desempenho e implementação

O núcleo usa a API `SendInput` do Windows para enviar os eventos. O agendamento usa um relógio de alta resolução, espera cancelável e resolução de temporizador de 1 ms enquanto está ativo. A interface continua separada do loop de entrada. No modo Pontos, um hook de mouse registra os cliques reais fora do aplicativo e ignora eventos simulados; a reprodução usa coordenadas absolutas da área de trabalho virtual, inclusive em múltiplos monitores.

A faixa configurável é de **500 a 5.000 CPS**, sem limite de duração ou total de cliques. Em taxas altas, o agendador pode ocupar bastante CPU. A taxa efetiva depende do Windows, da carga da máquina e do aplicativo que recebe os eventos; o contador representa entradas enviadas com sucesso pelo Windows, não confirma que cada clique foi processado pelo jogo ou site.

## Compilar a partir do código

Para desenvolver ou recompilar, instale o SDK do .NET 10 e execute nesta pasta:

```powershell
dotnet publish AutoClicker.csproj -c Release -r win-x64 --self-contained true -o release-5k
```

O resultado será `release-5k/AutoClicker.exe`, em pasta separada para não substituir uma instância anterior que ainda esteja aberta nem compartilhar a configuração dela.

## Observações

- Funciona apenas no Windows; o executável fornecido é para sistemas x64.
- Janelas abertas como administrador podem bloquear entradas de um aplicativo sem elevação. Se necessário, execute ambos com o mesmo nível de permissão.
- Se um atalho estiver reservado por outro programa, escolha outro na interface.
- Fechar o aplicativo interrompe os cliques. Ele não reinicia automaticamente ao ser aberto novamente.
