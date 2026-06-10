using System.Collections.Generic;
using UnityEngine;
using Oxide.Core;
using Oxide.Core.Plugins;
using System.Linq;
using Oxide.Core.Configuration;

namespace Oxide.Plugins
{
    [Info("BloodHunt", "VitorVmax", "1.0.7")]
    [Description("Rastreia a Bolsa de Sangue no mapa e salva a chave Pix dos jogadores.")]
    public class BloodHunt : RustPlugin
    {
        private const string BloodShortname = "blood";
        private BaseEntity mapMarker;
        private Timer updateTimer;
        private bool bloodDropped = false; // Controla se o blood já foi dropado

        // Estrutura de Dados para salvar as chaves PIX
        private class PluginData
        {
            public Dictionary<ulong, string> JogadoresPix = new Dictionary<ulong, string>();
            public ulong UltimoPortadorId = 0;
            public string UltimoPortadorNome = "Nenhum";
        }

        private PluginData data;
        private DynamicConfigFile dataFile;

        #region Inicialização e Hooks

        void Loaded()
        {
            dataFile = Interface.Oxide.DataFileSystem.GetFile("BloodHunt_Data");
            try
            {
                data = dataFile.ReadObject<PluginData>();
            }
            catch
            {
                data = new PluginData();
            }
        }

        void OnServerInitialized()
        {
            updateTimer = timer.Every(5f, UpdateMarkerPosition);
            UpdateMarkerPosition();
            bloodDropped = false; // Inicia permitindo drop
            Puts("[BloodHunt] Plugin inicializado!");
        }

        void OnUnload()
        {
            if (updateTimer != null) updateTimer.Destroy();
            RemoverMarcador();
            SalvarDados();
            Puts("[BloodHunt] Plugin descarregado!");
        }

        #endregion

        #region Comandos de Chat

        [ChatCommand("pix")]
        void CmdRegistrarPix(BasePlayer player, string command, string[] args)
        {
            if (args.Length == 0)
            {
                player.ChatMessage("<color=#00e676>[Pix]</color> Use: <color=#ffeb3b>/pix SUA_CHAVE_AQUI</color>");
                return;
            }

            string chavePix = string.Join(" ", args);
            data.JogadoresPix[player.userID] = chavePix;
            SalvarDados();

            player.ChatMessage($"<color=#00e676>[Pix]</color> Chave registrada com sucesso: <color=#ffeb3b>{chavePix}</color>");
        }

        [ChatCommand("portador")]
        void CmdVerPortador(BasePlayer player, string command, string[] args)
        {
            player.ChatMessage($"<color=#ff1744>[Evento]</color> O último portador registrado foi: <color=#ffeb3b>{data.UltimoPortadorNome}</color>");
        }

        [ChatCommand("debugblood")]
        void CmdDebugBlood(BasePlayer player, string command, string[] args)
        {
            bloodDropped = false; // Reset
            player.ChatMessage($"<color=#ffeb3b>[Debug]</color> bloodDropped resetado para false");
            Puts("[BloodHunt] Debug: bloodDropped foi resetado!");
        }

        [ChatCommand("testblood")]
        void CmdTestBlood(BasePlayer player, string command, string[] args)
        {
            Puts("[BloodHunt] TESTE: Enviando blood para o player...");
            
            ItemDefinition bloodDef = ItemManager.FindItemDefinition(BloodShortname);
            if (bloodDef == null) 
            {
                Puts("[BloodHunt] TESTE ERRO: Item 'blood' não encontrado!");
                player.ChatMessage("<color=#ff1744>[BloodHunt]</color> ERRO: Item 'blood' não encontrado!");
                return;
            }

            var bloodItem = ItemManager.Create(bloodDef, 1);
            if (bloodItem != null)
            {
                if (bloodItem.MoveToContainer(player.inventory.containerMain))
                {
                    Puts($"[BloodHunt] TESTE: Blood adicionado ao inventário de {player.displayName}!");
                    player.ChatMessage("<color=#00e676>[BloodHunt]</color> TESTE: Blood adicionado ao seu inventário!");
                }
                else
                {
                    Puts($"[BloodHunt] TESTE: Inventário cheio, dropando no chão...");
                    bloodItem.Drop(player.transform.position, Vector3.up * 1f);
                    player.ChatMessage("<color=#ffeb3b>[BloodHunt]</color> TESTE: Seu inventário está cheio, blood dropado no chão!");
                }
            }
        }

        #endregion

        #region Lógica do Marcador

        private void UpdateMarkerPosition()
        {
            Vector3 posicaoAtual = Vector3.zero;
            bool itemEncontrado = false;
            string nomeDoPortador = "";
            ulong idDoPortador = 0;

            // Obter o item ID do sangue
            ItemDefinition bloodDef = ItemManager.FindItemDefinition(BloodShortname);
            if (bloodDef == null)
            {
                Puts("Erro: Item 'blood' não encontrado!");
                return;
            }

            // Procurar em todos os players
            foreach (var player in BasePlayer.allPlayerList)
            {
                if (player == null || player.inventory == null) continue;

                // Verificar inventário do player
                var bloodItem = player.inventory.FindItemByItemID(bloodDef.itemid);
                if (bloodItem != null)
                {
                    posicaoAtual = player.transform.position;
                    nomeDoPortador = player.displayName;
                    idDoPortador = player.userID;
                    itemEncontrado = true;
                    break;
                }
            }

            // Se não encontrou em nenhum player, procurar em containers (baús, fornalhas, etc)
            if (!itemEncontrado)
            {
                foreach (var entity in BaseNetworkable.serverEntities)
                {
                    if (entity == null) continue;

                    var container = entity as StorageContainer;
                    if (container != null && container.inventory != null)
                    {
                        var bloodItem = container.inventory.FindItemByItemID(bloodDef.itemid);
                        if (bloodItem != null)
                        {
                            posicaoAtual = entity.transform.position;
                            nomeDoPortador = "Guardado em uma base";
                            itemEncontrado = true;
                            bloodDropped = true; // Marca que o blood está no mapa
                            break;
                        }
                    }
                }
            }

            if (itemEncontrado)
            {
                CriarOuMoverMarcador(posicaoAtual);
                
                // Se um player estiver segurando, atualiza o vencedor atual
                if (idDoPortador != 0)
                {
                    data.UltimoPortadorId = idDoPortador;
                    data.UltimoPortadorNome = nomeDoPortador;
                    SalvarDados();
                }
            }
        }

        private void CriarOuMoverMarcador(Vector3 posicao)
        {
            if (mapMarker == null || mapMarker.IsDestroyed)
            {
                mapMarker = GameManager.server.CreateEntity("assets/prefabs/tools/map/genericradiusmarker.prefab", posicao);
                
                if (mapMarker != null)
                {
                    var marker = mapMarker.GetComponent<MapMarkerGenericRadius>();
                    marker.radius = 15f; // TODO: Ajustar o raio conforme necessário
                    marker.color1 = Color.red;
                    marker.color2 = Color.black;
                    marker.alpha = 0.8f;
                    mapMarker.Spawn();
                }
            }
            else
            {
                mapMarker.transform.position = posicao;
                mapMarker.TransformChanged();
            }
        }

        private void RemoverMarcador()
        {
            if (mapMarker != null && !mapMarker.IsDestroyed)
            {
                mapMarker.Kill();
                mapMarker = null;
            }
        }

        private void SalvarDados()
        {
            dataFile.WriteObject(data);
        }

        #endregion

        #region Auto-Drop Blood Logic

        // Hook executado quando uma entidade combat é destruída (barris, containers, etc)
        void OnEntityKilled(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null)
            {
                Puts("[BloodHunt] OnEntityKilled: Entity é nulo!");
                return;
            }

            string prefabName = entity.ShortPrefabName;
            Puts($"[BloodHunt] OnEntityKilled chamado: {prefabName} | bloodDropped: {bloodDropped}");
            
            // NÃO retornar se bloodDropped for true - deixar o loop rodar
            if (bloodDropped)
            {
                Puts($"[BloodHunt] Blood já foi dropado, ignorando...");
                return;
            }

            if (!prefabName.Contains("barrel") && !prefabName.Contains("oil_barrel")) 
            {
                return;
            }

            Puts($"[BloodHunt] ✓ Barril detectado: {prefabName}");

            // Obter o player que destruiu o barril
            BasePlayer attacker = null;
            if (info != null)
            {
                attacker = info.InitiatorPlayer;
                Puts($"[BloodHunt] Info disponível, attacker: {attacker?.displayName}");
            }
            else
            {
                Puts("[BloodHunt] ⚠ Info é nulo! Tentando encontrar player próximo...");
                
                // Tentar encontrar player próximo ao barril
                Collider[] colliders = Physics.OverlapSphere(entity.transform.position, 5f);
                foreach (var collider in colliders)
                {
                    var player = collider.GetComponent<BasePlayer>();
                    if (player != null && !player.IsNpc)
                    {
                        attacker = player;
                        Puts($"[BloodHunt] Player encontrado próximo: {player.displayName}");
                        break;
                    }
                }
            }

            if (attacker == null || attacker.inventory == null)
            {
                Puts("[BloodHunt] ERRO: Não foi possível encontrar o player!");
                return;
            }

            ItemDefinition bloodDef = ItemManager.FindItemDefinition(BloodShortname);
            if (bloodDef == null) 
            {
                Puts("[BloodHunt] ERRO: Item 'blood' não encontrado!");
                return;
            }

            // Criar o blood e adicionar ao inventário do player
            var bloodItem = ItemManager.Create(bloodDef, 1);
            if (bloodItem != null)
            {
                if (bloodItem.MoveToContainer(attacker.inventory.containerMain))
                {
                    bloodDropped = true;
                    Puts($"[BloodHunt] ✓ Blood enviado para {attacker.displayName}!");
                    attacker.ChatMessage($"<color=#ff1744>[BloodHunt]</color> Parabéns! Você ganhou o <color=#ff1744>BLOOD</color>!");
                    
                    // Reset após 10 minutos (600s)
                    timer.Once(600f, () => {
                        bloodDropped = false;
                        Puts($"[BloodHunt] ✓ Blood drop cooldown finalizado!");
                    });
                }
                else
                {
                    // Se o inventário estiver cheio, dropar no chão
                    bloodItem.Drop(attacker.transform.position, Vector3.up * 1f);
                    bloodDropped = true;
                    Puts($"[BloodHunt] ⚠ Inventário cheio! Blood dropado no chão.");
                    attacker.ChatMessage($"<color=#ff1744>[BloodHunt]</color> Inventário cheio! O blood foi dropado no chão!");
                    
                    // Reset após 10 minutos (600s)
                    timer.Once(600f, () => {
                        bloodDropped = false;
                        Puts($"[BloodHunt] ✓ Blood drop cooldown finalizado!");
                    });
                }
            }
            else
            {
                Puts("[BloodHunt] ERRO: Falha ao criar o item blood!");
            }
        }

        #endregion
    }
}
