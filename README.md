# AutoClicker

Autoclicker para Windows feito em **C#**, com interface própria e dois modos: cliques do mouse ou pressionamentos de uma tecla. Depois de iniciado, repete a ação continuamente até você apertar **Parar**, usar o atalho de parada ou fechar o aplicativo.

## Baixar e usar

1. Abra [`release/AutoClicker.exe`](release/AutoClicker.exe).
2. Escolha **Mouse** ou **Teclado**.
3. Ajuste a velocidade (1 a 30 ações por segundo), o atraso inicial e o botão/tecla desejado. No modo Teclado, clique no campo da tecla e pressione a tecla que será repetida.
4. Clique em **Iniciar** ou use **F6**. Para encerrar, clique em **Parar** ou use **F7**. Para trocar um atalho, clique no campo **Iniciar** ou **Parar** e pressione uma tecla de F2 a F12.

O `.exe` publicado é **independente**: em um Windows 64 bits, não é necessário instalar Python, .NET ou bibliotecas adicionais. As preferências são salvas automaticamente em `settings.json`, na mesma pasta do executável.

## Controles

| Opção | O que faz |
| --- | --- |
| Mouse | Repete cliques esquerdo, direito ou do meio na posição atual do cursor. |
| Teclado | Repete a tecla capturada no campo na janela que estiver em foco. |
| Velocidade | Define de 1 a 30 ações por segundo. Pode ser alterada durante a execução. |
| Atraso inicial | Dá de 0 a 10 segundos para posicionar o cursor ou trocar de janela antes da primeira ação. |
| Atalhos globais | Clique em cada campo e pressione uma tecla de F2 a F12 para iniciar ou parar. Funcionam mesmo com outra janela em foco. |

O aplicativo impede que a tecla repetida seja igual a um dos atalhos de início ou parada. Os atalhos padrão são **F6 para iniciar** e **F7 para parar**.

## Desempenho e implementação

O núcleo usa a API `SendInput` do Windows para enviar eventos de mouse e teclado. O loop foi feito com tarefas assíncronas e espera cancelável, sem ficar consumindo CPU em uma espera contínua. A interface atualiza os contadores em intervalos curtos, separada da geração das entradas. A velocidade é limitada a 30 ações por segundo para manter o controle previsível.

O projeto é escrito em C# com Windows Forms e compilado com .NET 10 como **arquivo único, autocontido, para Windows x64**. O código-fonte fica nesta pasta; o usuário final só precisa do executável em `release`.

## Compilar a partir do código

Para desenvolver ou recompilar, instale o SDK do .NET 10 e execute nesta pasta:

```powershell
dotnet publish AutoClicker.csproj -c Release -r win-x64 --self-contained true -o release
```

O arquivo final será `release/AutoClicker.exe`.

## Observações

- O app funciona apenas no Windows. O executável fornecido é para sistemas 64 bits.
- Janelas abertas como administrador podem bloquear entradas de um app sem elevação; nesse caso, execute o AutoClicker com o mesmo nível de permissão.
- Se F6/F7 já estiverem reservadas por outro programa, selecione outros atalhos na interface.
- Fechar o AutoClicker interrompe a execução. Ele não reinicia os cliques automaticamente ao ser aberto novamente.
