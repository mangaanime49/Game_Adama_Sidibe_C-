using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace ASTrouverNombreCaché
{
    internal static class Program
    {
        private const int FACILE_MIN = 1;
        private const int FACILE_MAX = 10;
        private const int ECART_MIN_DIFFICILE = 10;   // (max - min) doit être > 10
        private const int LONGUEUR_SAISIE_MAX = 30;   // au-delà, la saisie est rejetée
        private const int LARGEUR = 44;               // largeur de l'affichage

        private static readonly Random Alea = new Random();

        // Entier strict : '-' optionnel puis 1 à 10 chiffres ASCII
        private static readonly Regex EntierStrict =
            new Regex(@"^-?[0-9]{1,10}$", RegexOptions.Compiled);

        // Deux entiers stricts séparés par des espaces, ou par ';' ou ','
        private static readonly Regex BornesStrictes =
            new Regex(@"^(-?[0-9]{1,10})(?:\s*[;,]\s*|\s+)(-?[0-9]{1,10})$", RegexOptions.Compiled);

        // Statistiques de la session
        private static double? meilleureNote = null;
        private static int partiesGagnees = 0;

        // Évite d'afficher deux fois le bilan (double Ctrl+C)
        private static int sortieEnCours = 0;

        private enum Resultat { Gagne, Quitte }

        private static void Main()
        {
            ConfigurerConsole();
            Console.CancelKeyPress += SurCtrlC;

            while (true)
            {
                // (2) Menu
                AfficherTitre();
                AfficherMessage("Choisissez 'F'-Facile 'D'-Difficile 'Q'-Quitter", ConsoleColor.Cyan);

                // (3), (5) Le choix doit être exactement F, D ou Q
                string choix;
                while (true)
                {
                    choix = Lire(out string? anomalie).ToUpperInvariant();
                    if (choix == "F" || choix == "D" || choix == "Q")
                        break;

                    AfficherMessage("Choix 'F', 'D' ou 'Q'", ConsoleColor.Yellow);
                    AfficherAnomalie(anomalie);
                }

                // (4) Quitter : effacer l'écran, puis afficher le bilan (après l'effacement)
                if (choix == "Q")
                {
                    EffacerEcran();
                    AfficherBilanFinal();
                    return;
                }

                if (choix == "F")
                    JouerFacile();
                else
                    JouerDifficile();
            }
        }

        // Ctrl+C / Ctrl+Break : bilan puis fermeture, une seule fois
        private static void SurCtrlC(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;

            if (Interlocked.Exchange(ref sortieEnCours, 1) == 1)
                return;

            AfficherMessage(string.Empty);
            AfficherBilanFinal();
            Environment.Exit(0);
        }

        // ------------------------------------------------------------------
        // Mode FACILE : intervalle fixe [1, 10]
        // ------------------------------------------------------------------
        private static void JouerFacile()
        {
            while (true)
            {
                Resultat resultat = JouerManche(
                    FACILE_MIN, FACILE_MAX, facile: true,
                    invite: $"choisissez un nombre entre [{FACILE_MIN}, {FACILE_MAX}] ou 'Q'-Quitter",
                    messageHorsBornes: $"réponse doit être comprise entre {FACILE_MIN} et {FACILE_MAX}");

                if (resultat == Resultat.Quitte)
                {
                    // (6c) effacer l'écran, afficher la meilleure note, retour au menu
                    EffacerEcran();
                    AfficherStatistiques();
                    return;
                }
                // Gagné : nouvelle manche (retour à 6-a)
            }
        }

        // ------------------------------------------------------------------
        // Mode DIFFICILE : bornes choisies par le joueur
        // ------------------------------------------------------------------
        private static void JouerDifficile()
        {
            while (true)
            {
                // (7a) à (7d) : saisie des bornes
                AfficherSeparateur();
                AfficherMessage("Entrez les bornes min et max de l'intervalle, ou 'Q'-Quitter", ConsoleColor.Cyan);

                int min, max;
                while (true)
                {
                    string saisie = Lire(out string? anomalie);

                    if (EstQuitter(saisie))
                    {
                        // (7c) effacer l'écran, afficher la meilleure note, retour au menu
                        EffacerEcran();
                        AfficherStatistiques();
                        return;
                    }

                    string? detail = DetailBornes(saisie, out min, out max);
                    if (detail == null)
                        break;

                    AfficherMessage("Choisissez 2 nombres entiers et (max – min) doit être > à 10 ", ConsoleColor.Yellow);
                    AfficherMessage(detail, ConsoleColor.Yellow);
                    AfficherAnomalie(anomalie);
                }

                // (7e) à (7k) : la manche
                Resultat resultat = JouerManche(
                    min, max, facile: false,
                    invite: $"choisissez un nombre entre [{min}, {max}] ou 'Q'-Quitter",
                    messageHorsBornes: $"Choix doit être compris entre {min} et {max}");

                if (resultat == Resultat.Quitte)
                    EffacerEcran();   // (7h) retour à (7a)
                // Gagné : l'écran a déjà été effacé, retour à (7a)
            }
        }

        // ------------------------------------------------------------------
        // Une manche : un nombre caché, plusieurs essais (commun aux 2 modes)
        // ------------------------------------------------------------------
        private static Resultat JouerManche(long min, long max, bool facile,
                                            string invite, string messageHorsBornes)
        {
            // Nombre caché tiré une seule fois par manche, bornes incluses
            long cache = Alea.NextInt64(min, max + 1);
            long etendue = max - min + 1;
            var essais = new List<long>();

            AfficherSeparateur();
            AfficherMessage(facile ? "Mode FACILE" : $"Mode DIFFICILE [{min}, {max}]", ConsoleColor.Magenta);
            AfficherMessage(invite, ConsoleColor.Cyan);

            while (true)
            {
                string saisie = Lire(out string? anomalie);

                if (EstQuitter(saisie))
                    return Resultat.Quitte;

                // Pas un entier valide (vide, lettre, décimal, trop long...)
                if (!TryLireEntier(saisie, out long choix))
                {
                    AfficherMessage("Entrée invalide : tapez un nombre entier ou 'Q'-Quitter", ConsoleColor.Yellow);
                    AfficherAnomalie(anomalie);
                    if (!facile)
                        AfficherMessage(invite, ConsoleColor.Cyan);
                    continue;
                }

                // Entier hors de [min, max]
                if (choix < min || choix > max)
                {
                    AfficherMessage(messageHorsBornes, ConsoleColor.Yellow);
                    if (!facile)
                        AfficherMessage(invite, ConsoleColor.Cyan);
                    continue;
                }

                // Nombre déjà proposé : refusé, non compté comme essai
                if (essais.Contains(choix))
                {
                    AfficherMessage($"Vous avez déjà proposé {choix}. Réponses déjà données : {string.Join(", ", essais)}",
                                    ConsoleColor.Yellow);
                    if (!facile)
                        AfficherMessage(invite, ConsoleColor.Cyan);
                    continue;
                }

                essais.Add(choix);

                if (choix == cache)
                {
                    partiesGagnees++;

                    double note = CalculerNote(etendue, essais.Count);
                    if (meilleureNote == null || note > meilleureNote)
                        meilleureNote = note;

                    AfficherMessage(new string('*', LARGEUR), ConsoleColor.Green);
                    AfficherMessage("vous avez gagné", ConsoleColor.Green);
                    AfficherMessage(new string('*', LARGEUR), ConsoleColor.Green);
                    AfficherMessage($"Essais : {essais.Count}", ConsoleColor.Gray);
                    AfficherMessage($"Note   : {FormaterNote(note)}", CouleurNote(note));
                    AfficherMessage($"Total  : {TexteParties(partiesGagnees)}", ConsoleColor.Gray);

                    bool quitter = DemanderRejouer();

                    EffacerEcran();
                    return quitter ? Resultat.Quitte : Resultat.Gagne;
                }

                if (facile)
                {
                    AfficherMessage($"vous avez perdu - réponses déjà données : {string.Join(", ", essais)}",
                                    ConsoleColor.Red);
                }
                else
                {
                    AfficherMessage("vous avez perdu", ConsoleColor.Red);
                    AfficherMessage(invite, ConsoleColor.Cyan);
                }
            }
        }

        // Pause après une victoire : seuls Entrée (rejouer) et Q (quitter) sont acceptés.
        // Retourne true si le joueur veut quitter.
        private static bool DemanderRejouer()
        {
            AfficherMessage("Appuyez sur Entrée pour rejouer, ou tapez 'Q' pour quitter", ConsoleColor.DarkGray);

            while (true)
            {
                string saisie = Lire(out string? anomalie);

                if (anomalie == null && saisie.Length == 0)
                    return false;

                if (EstQuitter(saisie))
                    return true;

                AfficherMessage("Tapez uniquement Entrée (rejouer) ou 'Q' (quitter).", ConsoleColor.Yellow);
                AfficherAnomalie(anomalie);
            }
        }

        // ------------------------------------------------------------------
        // Notes et statistiques
        // ------------------------------------------------------------------

        // Note = (étendue - essais) / étendue, en pourcentage. Ex. [1, 10], 3 essais : 70 %.
        private static double CalculerNote(long etendue, int nombreEssais)
        {
            double note = (double)(etendue - nombreEssais) / etendue * 100.0;
            return Math.Max(0.0, note);
        }

        private static string FormaterNote(double note)
            => note.ToString("0.##", CultureInfo.InvariantCulture) + " %";

        private static ConsoleColor CouleurNote(double note)
            => note >= 70 ? ConsoleColor.Green : note >= 40 ? ConsoleColor.Yellow : ConsoleColor.Red;

        private static string TexteParties(int nombre)
            => nombre <= 1 ? $"{nombre} partie gagnée" : $"{nombre} parties gagnées";

        // Meilleure note + parties gagnées (retour au menu)
        private static void AfficherStatistiques()
        {
            if (meilleureNote == null)
                AfficherMessage("Aucune partie gagnée pour le moment.", ConsoleColor.Yellow);
            else
                AfficherMessage($"Votre meilleure note : {FormaterNote(meilleureNote.Value)}", ConsoleColor.Green);

            AfficherMessage($"Parties gagnées : {partiesGagnees}", ConsoleColor.Green);
        }

        // Bilan affiché à la sortie du programme
        private static void AfficherBilanFinal()
        {
            AfficherMessage("╔" + new string('═', LARGEUR - 2) + "╗", ConsoleColor.Cyan);
            AfficherMessage(Centrer("Merci d'avoir joué !"), ConsoleColor.Cyan);
            AfficherMessage("╚" + new string('═', LARGEUR - 2) + "╝", ConsoleColor.Cyan);

            string pluriel = partiesGagnees > 1 ? "s" : string.Empty;
            AfficherMessage($"Vous avez gagné {partiesGagnees} partie{pluriel} pendant cette session.",
                            partiesGagnees > 0 ? ConsoleColor.Green : ConsoleColor.Yellow);

            if (meilleureNote != null)
                AfficherMessage($"Votre meilleure note : {FormaterNote(meilleureNote.Value)}", ConsoleColor.Green);
        }

        // ------------------------------------------------------------------
        // ENTRÉES : tout passe par Lire() puis par une validation stricte
        // ------------------------------------------------------------------

        // Retourne :
        //  - "Q" si le flux est fermé (Ctrl+Z / Ctrl+D) ou illisible
        //  - "" avec une anomalie expliquée si la saisie est trop longue ou contient des caractères de contrôle
        //  - sinon la saisie sans espaces de début/fin (anomalie = null)
        private static string Lire(out string? anomalie)
        {
            anomalie = null;
            Ecrire("> ", ConsoleColor.Yellow, false);

            string? saisie;
            try
            {
                saisie = Console.ReadLine();
            }
            catch (IOException)
            {
                return "Q";
            }

            if (saisie == null)
                return "Q";

            saisie = saisie.Trim();

            if (saisie.Length > LONGUEUR_SAISIE_MAX)
            {
                anomalie = $"Saisie trop longue (maximum {LONGUEUR_SAISIE_MAX} caractères).";
                return string.Empty;
            }

            foreach (char c in saisie)
            {
                if (char.IsControl(c))
                {
                    anomalie = "Les caractères non imprimables sont refusés.";
                    return string.Empty;
                }
            }

            return saisie;
        }

        private static bool EstQuitter(string saisie)
            => saisie.Equals("Q", StringComparison.OrdinalIgnoreCase);

        // Entier strict : refuse "+5", "5.0", "5,0", "1 2", "٣", "0x5", "abc"...
        private static bool TryLireEntier(string saisie, out long valeur)
        {
            valeur = 0;
            return EntierStrict.IsMatch(saisie)
                && long.TryParse(saisie, NumberStyles.AllowLeadingSign,
                                 CultureInfo.InvariantCulture, out valeur);
        }

        // Retourne null si les bornes sont valides, sinon la raison du refus
        private static string? DetailBornes(string saisie, out int min, out int max)
        {
            min = max = 0;

            Match m = BornesStrictes.Match(saisie);
            if (!m.Success)
                return "Il faut exactement 2 nombres entiers séparés par un espace, ex. : 1 50";

            if (!int.TryParse(m.Groups[1].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out min)
                || !int.TryParse(m.Groups[2].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out max))
                return "Un des nombres est trop grand ou trop petit.";

            if (min >= max)
                return "min doit être strictement inférieur à max.";

            long ecart = (long)max - min;   // en long : pas de dépassement
            if (ecart <= ECART_MIN_DIFFICILE)
                return $"L'écart max – min est de {ecart}, il doit être supérieur à {ECART_MIN_DIFFICILE}.";

            return null;
        }

        // ------------------------------------------------------------------
        // SORTIES : tout passe par Ecrire() / AfficherMessage()
        // ------------------------------------------------------------------

        // (8a) Méthode d'affichage des messages : le paramètre est le message à afficher
        // (la couleur est facultative et ne change que la présentation)
        private static void AfficherMessage(string message, ConsoleColor couleur = ConsoleColor.Gray)
        {
            Ecrire(message, couleur, true);
        }

        private static void AfficherAnomalie(string? anomalie)
        {
            if (anomalie != null)
                AfficherMessage(anomalie, ConsoleColor.Yellow);
        }

        private static void AfficherTitre()
        {
            AfficherMessage("╔" + new string('═', LARGEUR - 2) + "╗", ConsoleColor.Cyan);
            AfficherMessage(Centrer("TROUVER LE NOMBRE CACHÉ"), ConsoleColor.Cyan);
            AfficherMessage("╚" + new string('═', LARGEUR - 2) + "╝", ConsoleColor.Cyan);

            string note = meilleureNote == null ? "--" : FormaterNote(meilleureNote.Value);
            AfficherMessage($"Parties gagnées : {partiesGagnees}  |  Meilleure note : {note}", ConsoleColor.DarkGray);
        }

        // Ligne encadrée "║  texte centré  ║"
        private static string Centrer(string texte)
        {
            int interieur = LARGEUR - 2;
            int gauche = Math.Max(0, (interieur - texte.Length) / 2);
            int droite = Math.Max(0, interieur - gauche - texte.Length);
            return "║" + new string(' ', gauche) + texte + new string(' ', droite) + "║";
        }

        private static void AfficherSeparateur()
        {
            AfficherMessage(new string('─', LARGEUR), ConsoleColor.DarkGray);
        }

        // Écriture colorée protégée : une sortie fermée ou sans couleur ne fait pas planter
        private static void Ecrire(string texte, ConsoleColor couleur, bool saut)
        {
            try
            {
                Console.ForegroundColor = couleur;
                if (saut)
                    Console.WriteLine(texte);
                else
                    Console.Write(texte);
            }
            catch (Exception ex) when (ex is IOException || ex is PlatformNotSupportedException)
            {
                // Sortie indisponible : on ne plante pas
            }
            finally
            {
                try
                {
                    Console.ResetColor();
                }
                catch (Exception ex) when (ex is IOException || ex is PlatformNotSupportedException)
                {
                }
            }
        }

        private static void EffacerEcran()
        {
            try
            {
                Console.Clear();
            }
            catch (IOException)
            {
                // Sortie redirigée : Clear() impossible, on ignore
            }
        }

        private static void ConfigurerConsole()
        {
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
                Console.InputEncoding = Encoding.UTF8;
            }
            catch (IOException)
            {
                // Encodage non modifiable : on garde celui par défaut
            }
        }
    }
}