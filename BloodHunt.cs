using System.Collections.Generic;
using UnityEngine;
using Oxide.Core;
using Oxide.Core.Plugins;
using System.Linq;
using Oxide.Core.Configuration;

namespace Oxide.Plugins
{
    [Info("BloodHunt", "VitorVmax", "1.0.1")]
    [Description("Rastreia a Bolsa de Sangue no mapa e salva a chave Pix dos jogadores.")]
    public class BloodHunt : RustPlugin
    {
        private const string BloodShortname = "blood";
        private BaseEntity mapMarker;
        private Timer updateTimer;

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
        }

        void OnUnload()
        {
            if (updateTimer != null) updateTimer.Destroy();
            RemoverMarcador();
            SalvarDados();
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

        #endregion

        #region Lógica do Marcador

        private void UpdateMarkerPosition()
        {
            Vector3 posicaoAtual = Vector3.zero;
            bool itemEncontrado = false;
            string nomeDoPortador = "";
            ulong idDoPortador = 0;

            var instances = ItemManager.itemList.Where(i => i.info.shortname == BloodShortname).ToList();

            foreach (var inst in instances)
            {
                // 1. Está no inventário de alguém
                if (inst.GetRootContainer() != null)
                {
                    var player = inst.GetRootContainer().playerOwner;
                    if (player != null)
                    {
                        posicaoAtual = player.transform.position;
                        nomeDoPortador = player.displayName;
                        idDoPortador = player.userID;
                        itemEncontrado = true;
                        break;
                    }

                    // 2. Está dentro de um Baú/Fornalha
                    var entity = inst.GetRootContainer().entityOwner;
                    if (entity != null)
                    {
                        posicaoAtual = entity.transform.position;
                        nomeDoPortador = "Guardado em uma base";
                        itemEncontrado = true;
                        break;
                    }
                }
                // 3. Está dropado no chão
                else if (inst.GetWorldEntity() != null)
                {
                    posicaoAtual = inst.GetWorldEntity().transform.position;
                    nomeDoPortador = "Dropado no chão";
                    itemEncontrado = true;
                    break;
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
    }
}
